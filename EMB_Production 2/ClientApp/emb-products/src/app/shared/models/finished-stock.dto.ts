export interface FinishedStockRowDto {
  id: number;
  productName: string;
  branchName: string;
  qty: number;
  unit: string;
  isPosted: boolean;
  isReserved: boolean;
}

export interface FinishedStockDetailDto {
  id: number;
  productName: string;
  branchName: string;
  qty: number;
  unit: string;
  sourceType?: string;
  sourceId?: number;
  createdDateText?: string;
}
