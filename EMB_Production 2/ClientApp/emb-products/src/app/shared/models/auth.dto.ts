export interface User {
  Id: number;
  EmployeeId: number;
  UserName: string;
  Name: string;
  EmailAddress: string;
  RoleId: number;
  RoleName?: string;
  BranchId?: number;
  CompanyId?: number;
  FacialRecognition?: string;
  LastLoginDate?: string;
}

export interface LoginResponse {
  success: boolean;
  message?: string;
  data?: User | null;
}

export interface LogoutResponse {
  success: boolean;
  message?: string;
}
