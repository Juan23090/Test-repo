using System;
using System.Collections.Generic;

namespace EMA.App.Models;

public sealed class ChMSOption
{
    public int ChMSID { get; set; }
    public string? ChMSName { get; set; }
}

public sealed class SavingsCalculatorData
{
    public int EMAOrganizationID { get; set; }
    public string OrganizationName { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public decimal LaborCostPerHour { get; set; }
    public decimal MonthlySoftwareRental { get; set; }
    public int NumberOfMonths { get; set; }
    public int? ChMSID { get; set; }
    public string? ChMSName { get; set; }
    public bool InsufficientHistory { get; set; }
    public string Wording { get; set; } = string.Empty;
    public List<SavingsCalculatorDetailRow> Details { get; set; } = new();
    public decimal? ConfiguredEtransferRate { get; set; }
}

public sealed class SavingsCalculatorDetailRow
{
    public int EMAMethodOfGivingID { get; set; }
    public string Description { get; set; } = string.Empty;
    public short Sequence { get; set; }
    public int AnnualNumberOfTransactions { get; set; }
    public decimal AnnualValueOfTransactions { get; set; }
    public decimal LaborMinutesPerTransaction { get; set; }
    public string ChangeToEMAEtransfer { get; set; } = "N";
    public string IsActive { get; set; } = "Y";
    public decimal PercentBasedFee { get; set; }
    public decimal PerTransactionFee { get; set; }
}

public sealed class SavingsCalculatorPdfRequest
{
    public string OrganizationName { get; set; } = string.Empty;
    public string DatePeriodText { get; set; } = string.Empty;
    public string ChMSName { get; set; } = string.Empty;
    public decimal LaborCostPerHour { get; set; }
    public decimal MonthlySoftwareRental { get; set; }
    public int NumberOfMonths { get; set; }
    public string Wording { get; set; } = string.Empty;
    public List<SavingsCalculatorPdfRow> Details { get; set; } = new();
    public decimal SubtotalValue { get; set; }
    public int SubtotalTxs { get; set; }
    public double SubtotalMinutes { get; set; }
    public decimal SubtotalLaborCost { get; set; }
    public decimal SubtotalTotal { get; set; }
    public decimal SubtotalEmaCharge { get; set; }
    public decimal GrandTotal { get; set; }
    public decimal GrandSavings { get; set; }
    public string? FileName { get; set; }
}

public sealed class SavingsCalculatorPdfRow
{
    public string Description { get; set; } = string.Empty;
    public decimal Value { get; set; }
    public decimal PercentBasedFee { get; set; }
    public decimal PercentBasedCharge { get; set; }
    public int NumberOfTransactions { get; set; }
    public decimal PerTransactionFee { get; set; }
    public decimal TransactionBasedCharge { get; set; }
    public decimal LaborMinutes { get; set; }
    public decimal LaborCost { get; set; }
    public decimal TotalCost { get; set; }
    public string ChangeToEma { get; set; } = "N";
    public decimal EmaCharge { get; set; }
    public decimal PotentialSavings { get; set; }
}

public sealed class SavingsCalculatorEmailRequest
{
    public string RecipientEmail { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public SavingsCalculatorPdfRequest? CalculatorData { get; set; }
}

