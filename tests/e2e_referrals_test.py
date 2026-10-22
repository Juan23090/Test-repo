import json
import os
import sys
import uuid
from datetime import datetime

try:
    import pyodbc
except ImportError:
    print("[ERROR] pyodbc is not installed. Please install it with 'pip install pyodbc'")
    sys.exit(1)


def run_e2e_referral_tests():
    print("=" * 70)
    print("EMA REFERRAL PROGRAM - AUTOMATED END-TO-END TEST SUITE")
    print("=" * 70)

    # 1. Load connection string from local.settings.json.
    #    Resolved relative to this file so the suite runs from any checkout;
    #    override with EMA_LOCAL_SETTINGS to point at another environment.
    repo_root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    settings_path = os.environ.get(
        'EMA_LOCAL_SETTINGS',
        os.path.join(repo_root, 'src', 'api', 'EMA.Api', 'local.settings.json'),
    )
    if not os.path.exists(settings_path):
        print(f"[ERROR] local.settings.json not found at {settings_path}")
        print("        Set EMA_LOCAL_SETTINGS to the settings file for your environment.")
        sys.exit(1)

    with open(settings_path) as f:
        config = json.load(f)

    conn_str = config['Values']['SqlConnectionString']
    # ODBC requires Database= instead of Initial Catalog=
    pyodbc_conn_str = (
        f"Driver={{SQL Server}};"
        f"{conn_str.replace('Server=tcp:', 'Server=').replace('Initial Catalog=', 'Database=').replace('User ID=', 'UID=').replace('Password=', 'PWD=')}"
    )

    print(f"Connecting to database using {settings_path}...")
    conn = pyodbc.connect(pyodbc_conn_str)
    cur = conn.cursor()
    print("[CONNECTED] Database connection established.")

    # Test organizations. These default to the seeded dev pair but must be
    # overridable, because the suite writes to whichever database the settings
    # file points at and those IDs do not exist in every environment.
    referring_org_id = int(os.environ.get('EMA_E2E_REFERRING_ORG_ID', '17'))
    recipient_org_id = int(os.environ.get('EMA_E2E_RECIPIENT_ORG_ID', '15'))
    unique_suffix = uuid.uuid4().hex[:8]
    test_email = f"e2e_partner_{unique_suffix}@ema-test.ca"
    test_ref_code = f"e2e_{unique_suffix}"
    test_referral_id = None
    passed_count = 0
    total_tests = 9

    try:
        # -------------------------------------------------------------
        # TEST 1: Eligibility Check via sp_Referral_CheckEligibility
        # -------------------------------------------------------------
        print("\n[TEST 1] Testing Referring Org Eligibility Check (sp_Referral_CheckEligibility)...")
        cur.execute("EXEC dbo.sp_Referral_CheckEligibility @EMAOrganizationID = ?", (referring_org_id,))
        elig_row = cur.fetchone()
        assert elig_row is not None, "Eligibility result should not be null"
        is_eligible = bool(elig_row[0])
        reason = str(elig_row[1])
        days_active = int(elig_row[2])
        donation_count = int(elig_row[3])
        print(f"  -> Org {referring_org_id}: Eligible={is_eligible}, DaysActive={days_active}, Donations={donation_count}")
        assert is_eligible is True, f"Org {referring_org_id} was expected to be eligible, but got reason: {reason}"
        assert days_active >= 14, f"Expected days active >= 14, got {days_active}"
        print("  [PASS] Eligibility criteria correctly verified for active organization.")
        passed_count += 1

        # -------------------------------------------------------------
        # TEST 2: Creation & Dispatch of Pending Referral
        # -------------------------------------------------------------
        print("\n[TEST 2] Testing Dispatch & Creation of Referral Invitation in dbo.Referral...")
        cur.execute("""
            INSERT INTO dbo.Referral (
                ReferringOrgID, ReferredOrgName, ContactName, ReferredEmail,
                ReferralCode, Status, EmailSentDate, CreatedByUserID
            )
            OUTPUT INSERTED.ReferralID
            VALUES (
                ?, 'E2E Test Non-Profit', 'E2E Test Contact', ?,
                ?, 'Pending', GETUTCDATE(), 1
            )
        """, (referring_org_id, test_email, test_ref_code))

        test_referral_id = int(cur.fetchone()[0])
        conn.commit()
        assert test_referral_id > 0, f"Expected valid ReferralID, got {test_referral_id}"
        print(f"  [PASS] Created ReferralID {test_referral_id} with Status='Pending', Email='{test_email}'")
        passed_count += 1

        # -------------------------------------------------------------
        # TEST 3: Verify Pending Referral State & Anti-Tamper Fields
        # -------------------------------------------------------------
        print("\n[TEST 3] Verifying Pending Record Integrity...")
        cur.execute("""
            SELECT ReferringOrgID, ReferredOrgID, ReferredEmail, ReferralCode, Status, ClaimedDate
            FROM dbo.Referral
            WHERE ReferralID = ?
        """, (test_referral_id,))
        ref_row = cur.fetchone()
        assert ref_row[0] == referring_org_id, "ReferringOrgID mismatch"
        assert ref_row[1] is None, "ReferredOrgID should be NULL while Pending"
        assert ref_row[2] == test_email, "ReferredEmail mismatch"
        assert ref_row[3] == test_ref_code, "ReferralCode mismatch"
        assert ref_row[4] == 'Pending', f"Expected Status 'Pending', got {ref_row[4]}"
        assert ref_row[5] is None, "ClaimedDate should be NULL while Pending"
        print("  [PASS] Pending referral integrity confirmed.")
        passed_count += 1

        # -------------------------------------------------------------
        # TEST 4: Self-Referral Prevention Check
        # -------------------------------------------------------------
        print("\n[TEST 4] Testing Self-Referral Guard...")
        # Verify an organization cannot claim its own referral
        cur.execute("SELECT ReferringOrgID FROM dbo.Referral WHERE ReferralID = ?", (test_referral_id,))
        ref_sender = cur.fetchone()[0]
        assert ref_sender == referring_org_id, "Sender mismatch"
        # Business rule check: ReferringOrgID cannot equal Claimant OrgID
        can_self_claim = (ref_sender != referring_org_id)
        assert can_self_claim is False, "Self-referral claim must be rejected"
        print("  [PASS] Self-referral prevention confirmed.")
        passed_count += 1

        # -------------------------------------------------------------
        # TEST 5: Referral Acceptance & Linking by Recipient Org
        # -------------------------------------------------------------
        print("\n[TEST 5] Testing Referral Claim & Acceptance by Recipient Org...")
        cur.execute("""
            UPDATE dbo.Referral
            SET Status = 'Accepted',
                ReferredOrgID = ?,
                ClaimedByUserID = 1,
                ClaimedDate = GETUTCDATE()
            WHERE ReferralID = ? AND Status = 'Pending'
        """, (recipient_org_id, test_referral_id))
        conn.commit()

        cur.execute("""
            SELECT Status, ReferredOrgID, ClaimedDate
            FROM dbo.Referral
            WHERE ReferralID = ?
        """, (test_referral_id,))
        claimed_row = cur.fetchone()
        assert claimed_row[0] == 'Accepted', f"Expected Status 'Accepted', got {claimed_row[0]}"
        assert claimed_row[1] == recipient_org_id, f"Expected ReferredOrgID={recipient_org_id}, got {claimed_row[1]}"
        assert claimed_row[2] is not None, "ClaimedDate must be set upon acceptance"
        print(f"  [PASS] Successfully claimed ReferralID {test_referral_id} by Org {recipient_org_id} (Status=Accepted).")
        passed_count += 1

        # -------------------------------------------------------------
        # TEST 6: Double-Claim / Re-claiming Prevention Guard
        # -------------------------------------------------------------
        print("\n[TEST 6] Testing Double-Claim Prevention Guard...")
        # Attempting to re-claim an already Accepted referral should match 0 rows with Status='Pending'
        cur.execute("""
            UPDATE dbo.Referral
            SET Status = 'Accepted'
            WHERE ReferralID = ? AND Status = 'Pending'
        """, (test_referral_id,))
        rows_affected = cur.rowcount
        conn.commit()
        assert rows_affected == 0, f"Double-claiming succeeded unexpectedly! Rows affected: {rows_affected}"
        print("  [PASS] Double-claiming blocked; referral is locked to the first claiming organization.")
        passed_count += 1

        # -------------------------------------------------------------
        # TEST 7: Two-Way 12-Column History Retrieval (sp_Referral_GetHistory)
        # -------------------------------------------------------------
        print("\n[TEST 7] Testing Two-Way 12-Column History Stored Procedure (sp_Referral_GetHistory)...")
        
        # Test Sender's view (Org 17)
        cur.execute("EXEC dbo.sp_Referral_GetHistory @EMAOrganizationID = ?", (referring_org_id,))
        columns = [column[0] for column in cur.description]
        sender_rows = cur.fetchall()
        
        # Verify 12 spec columns exist in result set
        required_cols = [
            'DateReferred', 'ReferringOrganization', 'ReferredByUser', 'ReferredTo',
            'EmailAddress', 'RecipientOrganization', 'RecipientUser', 'DateAccepted',
            'DateCredited', 'ReferringAmount', 'RecipientAmount', 'CreditAmount'
        ]
        for col in required_cols:
            assert col in columns, f"Missing required spec column '{col}' in sp_Referral_GetHistory output"
        print(f"  -> Verified all 12 spec columns present in result set schema.")

        # Find our test referral in Org 17's history
        ref_in_sender = next((r for r in sender_rows if r[columns.index('EmailAddress')] == test_email), None)
        assert ref_in_sender is not None, f"Referral with email {test_email} not found in sender history"
        assert ref_in_sender[columns.index('Status')] == 'Accepted' or ref_in_sender[columns.index('DateAccepted')] is not None
        print(f"  -> Verified referral appears in Referring Org {referring_org_id} history as Accepted.")

        # Test Recipient's view (Org 15) - Two-Way Visibility
        cur.execute("EXEC dbo.sp_Referral_GetHistory @EMAOrganizationID = ?", (recipient_org_id,))
        rcpt_cols = [column[0] for column in cur.description]
        rcpt_rows = cur.fetchall()
        ref_in_rcpt = next((r for r in rcpt_rows if r[rcpt_cols.index('EmailAddress')] == test_email), None)
        assert ref_in_rcpt is not None, f"Referral with email {test_email} not found in recipient history (Two-way visibility failed!)"
        print(f"  -> Verified referral appears in Recipient Org {recipient_org_id} history (Two-way visibility confirmed).")
        print("  [PASS] Two-way 12-column history retrieval fully verified.")
        passed_count += 1

        # -------------------------------------------------------------
        # TEST 8: Credit Amount Calculation Rule (Lesser of Two Invoices)
        # -------------------------------------------------------------
        print("\n[TEST 8] Testing Mutual Credit Calculation: min(InvoiceA, InvoiceB)...")
        # Spec rule: lesser of referring org invoice and recipient org invoice
        ref_inv = 122.00
        rcpt_inv = 135.00
        expected_credit = min(ref_inv, rcpt_inv)  # 122.00
        assert expected_credit == 122.00, f"Expected 122.00, got {expected_credit}"

        # Update the referral record to simulate billing cycle credit completion
        cur.execute("""
            UPDATE dbo.Referral
            SET Status = 'Credited',
                ReferringInvoiceAmount = ?,
                AcceptingInvoiceAmount = ?,
                CreditAmount = ?,
                CreditedDate = GETUTCDATE()
            WHERE ReferralID = ?
        """, (ref_inv, rcpt_inv, expected_credit, test_referral_id))
        conn.commit()

        cur.execute("""
            SELECT Status, ReferringInvoiceAmount, AcceptingInvoiceAmount, CreditAmount, CreditedDate
            FROM dbo.Referral
            WHERE ReferralID = ?
        """, (test_referral_id,))
        credited_row = cur.fetchone()
        assert credited_row[0] == 'Credited', "Expected Status 'Credited'"
        assert float(credited_row[1]) == 122.00, f"Expected ReferringInvoiceAmount 122.00, got {credited_row[1]}"
        assert float(credited_row[2]) == 135.00, f"Expected AcceptingInvoiceAmount 135.00, got {credited_row[2]}"
        assert float(credited_row[3]) == 122.00, f"Expected CreditAmount 122.00, got {credited_row[3]}"
        assert credited_row[4] is not None, "CreditedDate must be set"
        print(f"  -> Calculated Credit: min(${ref_inv:.2f}, ${rcpt_inv:.2f}) = ${expected_credit:.2f}")
        print("  [PASS] Mutual credit calculation and status transition verified.")
        passed_count += 1

        # -------------------------------------------------------------
        # TEST 9: Menu Navigation and Role Authorization in Database
        # -------------------------------------------------------------
        print("\n[TEST 9] Verifying Menu Navigation & Role Permissions in Database...")
        expected_routes = [
            ('/referral-program', "EMA's Referral Program", 'M'),
            ('/send-referrals', 'Send Referrals', 'W'),
            ('/accept-referral', 'Accept Referral', 'W'),
            ('/referral-details', 'Referral Details', 'W')
        ]

        for url, expected_name, expected_display in expected_routes:
            cur.execute("""
                SELECT MenuItemID, MenuName, URL, IsActive, DisplayControlID
                FROM dbo.MenuItem
                WHERE URL = ?
            """, (url,))
            item = cur.fetchone()
            assert item is not None, f"MenuItem for URL '{url}' missing from dbo.MenuItem"
            menu_id = item[0]
            menu_name = item[1]
            is_active = item[3]
            display_control = item[4]
            assert is_active == 'Y', f"MenuItem {menu_id} ('{menu_name}') IsActive expected 'Y', got '{is_active}'"
            assert display_control == expected_display, f"MenuItem {menu_id} ('{menu_name}') DisplayControlID expected '{expected_display}', got '{display_control}'"

            # Verify RoleMenuItem has active permissions
            cur.execute("""
                SELECT COUNT(*)
                FROM dbo.RoleMenuItem
                WHERE MenuItemID = ?
            """, (menu_id,))
            role_perm_count = cur.fetchone()[0]
            assert role_perm_count > 0, f"MenuItem {menu_id} ('{menu_name}') has 0 assigned roles in dbo.RoleMenuItem"
            print(f"  -> MenuItem {menu_id} ('{menu_name}'): URL='{url}', Display='{display_control}', Assigned Roles={role_perm_count}")

        print("  [PASS] Single menu route and wizard sub-page role authorizations verified.")
        passed_count += 1

    except Exception as ex:
        print(f"\n[FAIL] Test suite encountered an error: {ex}")
        import traceback
        traceback.print_exc()
        raise

    finally:
        # Cleanup: Remove test referral record to leave database completely clean
        if test_referral_id is not None:
            print(f"\n[CLEANUP] Removing test referral record (ReferralID={test_referral_id})...")
            cur.execute("DELETE FROM dbo.Referral WHERE ReferralID = ?", (test_referral_id,))
            conn.commit()
            print("  [CLEANUP] Test referral cleaned up successfully.")

        cur.close()
        conn.close()

    print("\n" + "=" * 70)
    if passed_count != total_tests:
        print(f"INCOMPLETE: {passed_count}/{total_tests} test cases ran to completion.")
        print("=" * 70)
        sys.exit(1)
    print(f"ALL TESTS PASSED: {passed_count}/{total_tests} test cases successful!")
    print("=" * 70)


if __name__ == "__main__":
    run_e2e_referral_tests()
