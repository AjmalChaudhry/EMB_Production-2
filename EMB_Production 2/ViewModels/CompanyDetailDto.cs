using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace EMB_Production.ViewModels
{
    public class CompanyDetailDto
    {
        public int CompanyID { get; set; }
        public string CompanyName { get; set; }
        public string NickName { get; set; }
        public string Email { get; set; }
        public string ContactNumber { get; set; }
        public string Address { get; set; }
        public string Logo { get; set; }
        public string Approval { get; set; }
        public bool Active { get; set; }
        public bool Deleted { get; set; }
        public string LicenseKey { get; set; }
        public string NTN { get; set; }
        public string STRN { get; set; }
        public string MasterPassword { get; set; }
        public string CultureCode { get; set; }
        public string CalendarCode { get; set; }
        public string Theme { get; set; }
        public int? AmountFormat { get; set; }
        public int? DecimalsInAmount { get; set; }
        public int? Decimal_Points { get; set; }
        public string CurrencyDefault { get; set; }
        public bool? ShowCurrencyName { get; set; }
        public bool? StopLogin { get; set; }
        public bool? TaxEnabled { get; set; }
        public int? PeriodId { get; set; }
        public string Language { get; set; }
        public DateTime? RecordTimeStamp { get; set; } // fixed
        public int? OperatingUserID { get; set; }
        public bool? AllowExtraP { get; set; }
        public bool? AllowRM { get; set; }
        public int? CreatedBy { get; set; }
        public DateTime? CreatedDate { get; set; }
        public int? UpdatedBy { get; set; }
        public DateTime? UpdatedDate { get; set; }
    }

}