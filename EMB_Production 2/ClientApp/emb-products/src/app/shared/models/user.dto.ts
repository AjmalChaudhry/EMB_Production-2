/** ---------------- Row-level view for tables ---------------- */
export interface UserRowViewModel {
  Id: number;
  EmployeeId?: number | null;
  UserName: string;

  /** Restore name fields for compatibility */
  FirstName?: string;
  LastName?: string;
  FullName?: string;
  Name?: string; // kept for backend mapping (e.g. "Muhammad Ajmal")

  EmailAddress?: string;
  MobilePrimary?: string;
  Landline?: string;
  Password?: string;

  BranchId?: number | null;
  BranchName?: string;

  RoleId?: number | null;
  RoleName?: string;

  CompanyId?: number | null;
  CompanyName?: string;

  DesignationId?: number | null;
  DesignationName?: string;

  Active?: boolean;
  DashboardAccess?: boolean;
  Approvel?: boolean | string | null;

  FacialRecognition?: string;

  /** Audit fields */
  CreatedBy?: number | null;
  CreatedDate?: string | Date | null;
  UpdatedBy?: number | null;
  UpdatedDate?: string | Date | null;
  DeletedBy?: number | null;
  DeletedDate?: string | Date | null;
  LastLoginDate?: string | Date | null;

  /** Computed display-friendly labels */
  ActiveLabel?: string;
  DashboardAccessLabel?: string;
  ApprovelLabel?: string;

  CreatedByName?: string;
  UpdatedByName?: string;
  DeletedByName?: string;

  CreatedDateFormatted?: string;
  UpdatedDateFormatted?: string;
  DeletedDateFormatted?: string;
  LastLoginDateFormatted?: string;

  PhotoUrl?: string;
}

/** ---------------- Detail view for forms/modals ---------------- */
export interface UserDetailViewModel {
  Id: number;
  EmployeeId?: string | number | null;
  UserName: string;

  /** Restore name fields for form bindings */
  FirstName?: string;
  LastName?: string;
  FullName?: string;
  Name?: string;

  EmailAddress?: string;
  MobilePrimary?: string;
  Landline?: string;
  Password?: string;

  BranchId?: number | null;
  RoleId?: number | null;
  CompanyId?: number | null;

  Active?: boolean;
  DashboardAccess?: boolean;
  Approvel?: boolean | string | null;
  FacialRecognition?: string;

  /** Audit */
  CreatedBy?: number | null;
  UpdatedBy?: number | null;
  CreatedDate?: string | Date | null;
  UpdatedDate?: string | Date | null;
  LastLoginDate?: string | Date | null;

  /** Computed / display-friendly */
  BranchName?: string;
  RoleName?: string;
  CompanyName?: string;
  CreatedByName?: string;
  UpdatedByName?: string;
  CreatedDateFormatted?: string;
  UpdatedDateFormatted?: string;
  LastLoginDateFormatted?: string;
  ActiveLabel?: string;
  DashboardAccessLabel?: string;
  ApprovelLabel?: string;
  PhotoUrl?: string;
}

/** ---------------- Optional paged response wrapper ---------------- */
export interface ApiResponseData<T> {
  success: boolean;
  message?: string;
  data?: T | T[] | null;
  items?: T[];
  total?: number;
}

/** ---------------- Optional page DTO for user table UI ---------------- */
export interface UsersPageViewModel {
  Search?: string;
  ActiveFilter?: string;

  Users: UserRowViewModel[];

  ShowCreate?: boolean;
  ShowEdit?: boolean;
  EditId?: number;

  NewUser?: Partial<UserDetailViewModel>;
  EditUser?: Partial<UserDetailViewModel>;

  RoleList?: { Id: number; Name: string }[];
  BranchList?: { Id: number; Name: string }[];
  CompanyList?: { CompanyID: number; CompanyName: string }[];
}
