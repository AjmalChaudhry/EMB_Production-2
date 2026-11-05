
export interface CompanyListViewModel {
  companyID: number;
  companyName: string;
  nickName: string;
  email: string;
  contactNumber: string;
  address: string;
  approval: string; // displayed as text in list
  active: boolean;
  deleted: boolean;
  recordTimeStamp?: string | null;
  logoPath?: string | null;
}

export interface CompanyDetailViewModel extends Omit<CompanyListViewModel, 'approval'> {
  approval: boolean;
  allowExtraP: boolean;
  taxEnabled: boolean;
  stopLogin: boolean;
  showCurrencyName: boolean;
  operatingUserID?: number | null;
  licenseKey?: string | null;
  ntn?: string | null;
  strn?: string | null;
  masterPassword: string;
  cultureCode: string;
  calendarCode: string;
  theme: string;
  periodId?: number | null;
  currencyDefault: string;
  decimalsInAmount: number;
  decimal_Points: number;
  language?: string | null;

  createdBy: string;
  createdDate: string | null;
  updatedBy: string;
  updatedDate: string | null;

  recordTime: string | null; // mapped from backend RecordTimeStamp
  logo: string | null;       // resolved path for display

  ownerName?: string;
  logoFile?: string;
}



// Form for create/update (editable only)
export interface CompanyForm {
  // Basic Info
  companyID?: number;
  companyName: string;
  nickName?: string;
  address?: string;
  email?: string;
  contactNumber?: string;

  // Status & control
  approval?: string;         // e.g. 'Pending', 'Approved'
  active?: boolean;
  deleted?: boolean;
  allowExtraP?: boolean;
  allowRM?: boolean;
  stopLogin?: boolean;

  // Ownership / Identification
  ownerName?: string;
  licenseKey?: string;
  ntn?: string;
  strn?: string;
  masterPassword?: string;

  // Localization & Theme
  cultureCode?: string;      // e.g. 'en-PK'
  calendarCode?: string;     // e.g. 'Gregorian'
  theme?: string;            // e.g. 'light', 'dark'

  // Currency / Formatting
  currencyDefault?: string;  // e.g. 'PKR'
  decimalsInAmount?: number;
  decimal_Points?: number;
  showCurrencyName?: boolean;
  taxEnabled?: boolean;
  periodId?: number;

  // File Handling
  logoFile?: File | null;    // for image uploads

  // Optional Metadata (not posted but can be shown)
  operatingUserID?: number;
  language?: string;
  recordTimeStamp?: string;
}

