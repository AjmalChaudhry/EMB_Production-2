using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace EMB_Production.ViewModels
{
    public class CustomerExcelRow
    {
        public int Id { get; set; }
        public string CustomerID { get; set; }
        public string Customer_Name { get; set; }
        public string Customer_City { get; set; }
        public string Customer_Country { get; set; }
        public string Branch { get; set; }
        public string Active { get; set; }
        public string Deleted { get; set; }
        public int? CreatedBy { get; set; }
        public DateTime? CreatedDate { get; set; }
        public int? UpdatedBy { get; set; }
        public DateTime? UpdatedDate { get; set; }
    }
}