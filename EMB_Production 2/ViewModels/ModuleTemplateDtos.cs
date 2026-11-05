// Models/Dto/ModuleTemplateDtos.cs
using System;
using System.Collections.Generic;

namespace EMB_Production.Models.Dto
{
    public class ModuleTemplateRowViewModel
    {
        public int Id { get; set; }

        public string Code { get; set; }
        public string Name { get; set; }

        public int? BranchId { get; set; }
        public string BranchName { get; set; }

        public bool? Active { get; set; }
        public bool? Deleted { get; set; }

        public DateTime? CreatedDate { get; set; }
        public DateTime? UpdatedDate { get; set; }
    }

    public class ModuleTemplateDetailViewModel : ModuleTemplateRowViewModel
    {
        public string Remarks { get; set; }
    }

    public class ModuleTemplateDto
    {
        public int Id { get; set; } // 0 for create

        public string Code { get; set; }
        public string Name { get; set; }
        public string Remarks { get; set; }

        public int? BranchId { get; set; }
        public bool Active { get; set; }
    }

    // If you already have shared ApiResponse, you can skip this region
    #region Api Response Wrappers (optional if already globally defined)

    public class ApiResponseBase
    {
        public bool Success { get; set; }
        public string Message { get; set; }
    }

    public class ApiResponseData<T> : ApiResponseBase
    {
        public T Data { get; set; }
    }

    public class ApiResponsePaged<T> : ApiResponseBase
    {
        public PagedResult<T> Data { get; set; }
    }

    public class PagedResult<T>
    {
        public List<T> Items { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int Total { get; set; }
    }

    #endregion
}
