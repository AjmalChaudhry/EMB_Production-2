using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace EMB_Production.ViewModels
{
    public class CustomerRowViewModel
    {
        public int Id { get; set; }
        public string CustomerId { get; set; }  // was Customer_Id
        public string Name { get; set; }
        public string NTN_Number { get; set; }
        public string STRN_Number { get; set; }
        public string Customer_Address { get; set; }
        public string Customer_City { get; set; }
        public string Customer_Country { get; set; }
        public string Customer_Email { get; set; }
        public string Customer_Email2 { get; set; }
        public string Customer_Phone { get; set; }
        public string Customer_Phone2 { get; set; }
        public string Customer_Mobile { get; set; }
        public string Customer_Fax { get; set; }
        public decimal? Customer_Credit_Limit_CAP { get; set; }
        public string Customer_Invoice_Period { get; set; }

        public string BranchName { get; set; }

        public bool Active { get; set; }
        public bool Deleted { get; set; }
        public int? CreatedBy { get; set; }
        public DateTime? CreatedDate { get; set; }
        public int? UpdatedBy { get; set; }
        public DateTime? UpdatedDate { get; set; }
    }
}

