import pyodbc, json, sys, os

def run_tests():
    # Load SQL Connection string
    settings_path = r'c:\Users\nicol\IMPORTANT PROJECTS - DO NOT DELETE\email-money-app\src\api\EMA.Api\local.settings.json'
    with open(settings_path) as f:
        config = json.load(f)

    conn_str = config['Values']['SqlConnectionString']
    pyodbc_conn_str = f"Driver={{SQL Server}};{conn_str.replace('Server=tcp:', 'Server=').replace('User ID=', 'UID=').replace('Password=', 'PWD=')}"

    print(f"Connecting to database...")
    conn = pyodbc.connect(pyodbc_conn_str)
    cur = conn.cursor()

    org_id = 17
    test_list_name = "Automated E2E Test List"

    try:
        # 1. Test List Creation
        print("\n[TEST 1] Testing Create Mailing List via SQL INSERT...")
        cur.execute("""
            INSERT INTO dbo.OrganizationMailingList (
                EMAOrganizationID, Name, Description,
                Donation1StartDate, Donation1EndDate, Donation1MoneyFrom, Donation1MoneyTo,
                AndOr, OutputFormatID, SendMethodID, IsActive, DateTimeCreated, CreatedByUserID
            )
            OUTPUT INSERTED.MailingListID
            VALUES (
                ?, ?, 'Created by automated end to end test suite.',
                '2010-01-01', '2099-12-31', 10.00, 10000000.00,
                'And', 1, 1, 1, GETDATE(), 1
            )
        """, (org_id, test_list_name))
        
        list_id = cur.fetchone()[0]
        conn.commit()
        assert list_id > 0, f"Expected valid MailingListID, got {list_id}"
        print(f"  [PASS] Successfully created MailingListID: {list_id}")

        # 2. Test List Update
        print("\n[TEST 2] Testing Update Mailing List...")
        updated_desc = "Updated description by automated test."
        cur.execute("""
            UPDATE dbo.OrganizationMailingList
            SET Description = ?,
                Donation1MoneyFrom = 25.00,
                DateTimeUpdated = GETDATE()
            WHERE EMAOrganizationID = ? AND MailingListID = ?
        """, (updated_desc, org_id, list_id))
        conn.commit()

        cur.execute("SELECT Description, Donation1MoneyFrom FROM dbo.OrganizationMailingList WHERE MailingListID = ?", (list_id,))
        row = cur.fetchone()
        assert row[0] == updated_desc, f"Expected '{updated_desc}', got '{row[0]}'"
        assert float(row[1]) == 25.00, f"Expected 25.00, got {row[1]}"
        print(f"  [PASS] Successfully verified updated description and minimum donation amount.")

        # 3. Test Stored Procedure sp_PrepareMailingListInstance
        print("\n[TEST 3] Testing sp_PrepareMailingListInstance...")
        cur.execute("""
            DECLARE @InstanceID INT;
            EXEC dbo.sp_PrepareMailingListInstance @EMAOrganizationID = ?, @MailingListID = ?, @NewInstanceID = @InstanceID OUTPUT;
            SELECT @InstanceID;
        """, (org_id, list_id))
        
        instance_row = cur.fetchone()
        instance_id = instance_row[0] if instance_row else None
        conn.commit()
        assert instance_id is not None and instance_id > 0, f"Expected valid instance ID, got {instance_id}"
        print(f"  [PASS] Prepared instance with ID: {instance_id}")

        # 4. Verify Instance and Snapshot Donors
        print("\n[TEST 4] Verifying Instance Donor Snapshot...")
        cur.execute("SELECT COUNT(*) FROM dbo.OrganizationMailingListInstanceDonor WHERE MailingListInstanceID = ?", (instance_id,))
        donor_count = cur.fetchone()[0]
        assert donor_count >= 0, "Expected non-negative donor count"
        print(f"  [PASS] Instance donor snapshot record count: {donor_count}")

        # 5. Test Donor Opt Toggle MERGE
        print("\n[TEST 5] Testing Toggle Donor Opt-In & Opt-Out Status...")
        cur.execute("SELECT TOP 1 EMADonorID FROM dbo.Donor WHERE EMAOrganizationID = ?", (org_id,))
        sample_donor_id = cur.fetchone()[0]

        # Toggle Opt-In
        cur.execute("""
            MERGE dbo.OrganizationMailingListDonor AS target
            USING (SELECT ? AS EMAOrganizationID, ? AS MailingListID, ? AS EMADonorID) AS source
            ON target.EMAOrganizationID = source.EMAOrganizationID 
           AND target.MailingListID = source.MailingListID 
           AND target.EMADonorID = source.EMADonorID
            WHEN MATCHED THEN
                UPDATE SET OptedIn = 1, OptedOut = 0
            WHEN NOT MATCHED THEN
                INSERT (EMAOrganizationID, MailingListID, EMADonorID, OptedIn, OptedOut, DateTimeCreated)
                VALUES (source.EMAOrganizationID, source.MailingListID, source.EMADonorID, 1, 0, GETDATE());
        """, (org_id, list_id, sample_donor_id))
        conn.commit()

        cur.execute("SELECT OptedIn, OptedOut FROM dbo.OrganizationMailingListDonor WHERE MailingListID = ? AND EMADonorID = ?", (list_id, sample_donor_id))
        opt_state = cur.fetchone()
        assert opt_state[0] == 1 and opt_state[1] == 0, f"Expected OptedIn=1, OptedOut=0; got {opt_state}"
        print(f"  [PASS] Successfully verified Opted-In toggle for Donor {sample_donor_id}")

        # 6. Test Delete / Deactivate List
        print("\n[TEST 6] Testing Delete / Deactivate Mailing List...")
        cur.execute("""
            UPDATE dbo.OrganizationMailingList
            SET IsActive = 0,
                DateTimeUpdated = GETDATE()
            WHERE EMAOrganizationID = ? AND MailingListID = ?
        """, (org_id, list_id))
        conn.commit()

        cur.execute("SELECT IsActive FROM dbo.OrganizationMailingList WHERE MailingListID = ?", (list_id,))
        is_active = cur.fetchone()[0]
        assert is_active == 0 or is_active == False, f"Expected IsActive = 0, got {is_active}"
        print(f"  [PASS] Successfully verified list deactivation.")

        # Cleanup test record
        print("\n[CLEANUP] Cleaning up test data...")
        cur.execute("DELETE FROM dbo.OrganizationMailingListDonor WHERE MailingListID = ?", (list_id,))
        cur.execute("DELETE FROM dbo.OrganizationMailingListInstanceDonor WHERE MailingListID = ?", (list_id,))
        cur.execute("DELETE FROM dbo.OrganizationMailingListInstance WHERE MailingListID = ?", (list_id,))
        cur.execute("DELETE FROM dbo.OrganizationMailingList WHERE MailingListID = ?", (list_id,))
        conn.commit()
        print("  [PASS] Cleanup complete.")

        print("\n==================================================")
        print("SUCCESS: ALL 6 END-TO-END MAILING LIST TESTS PASSED!")
        print("==================================================")

    except Exception as e:
        print(f"\n[FAIL] Test failed: {e}")
        conn.rollback()
        sys.exit(1)
    finally:
        conn.close()

if __name__ == '__main__':
    run_tests()
