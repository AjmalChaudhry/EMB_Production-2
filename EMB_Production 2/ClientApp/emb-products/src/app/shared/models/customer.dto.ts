// src/app/shared/models/customer.dto.ts

/** TABLE LIST VIEW */
export interface CustomerListViewModel {
  id: number;
  customerId: string;
  name: string;

  ntnNumber?: string | null;
  strnNumber?: string | null;

  customerAddress?: string | null;
  customerCity?: string | null;
  customerCountry?: string | null;

  customerEmail?: string | null;
  customerEmail2?: string | null;

  customerPhone?: string | null;
  customerPhone2?: string | null;
  customerMobile?: string | null;
  customerFax?: string | null;

  customerCreditLimitCap?: number | null;
  customerInvoicePeriod?: number | null;

  branchId?: number | null;
  branchName?: string | null;

  active: boolean;
  deleted?: boolean | null;

  createdDate?: string | null;
  updatedDate?: string | null;
}

/** DETAIL MODAL VIEW */
export interface CustomerDetailViewModel {
  id: number;

  customerId: string;
  name: string;

  ntnNumber: string | null;
  strnNumber: string | null;

  customerAddress: string | null;
  customerCity: string | null;
  customerCountry: string | null;

  customerEmail: string | null;
  customerEmail2: string | null;

  customerPhone: string | null;
  customerPhone2: string | null;
  customerMobile: string | null;
  customerFax: string | null;

  customerCreditLimitCap: number | null;
  customerInvoicePeriod: number | null;

  branchId: number | null;
  branchName: string | null;

  active: boolean;
  deleted: boolean | null;

  createdBy: number | null;
  createdDateFormatted: string | null;

  updatedBy: number | null;
  updatedDateFormatted: string | null;

  recordTimeStamp: string | null;
}

