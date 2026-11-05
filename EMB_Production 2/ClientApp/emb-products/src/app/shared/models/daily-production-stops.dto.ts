export interface DailyProductionStopRowViewModel {
  Id: number;
  ProductionId: number;
  ProductionLabel?: string;

  StopStart: string | null;
  StopEnd: string | null;

  Reason?: string | null;
  CreatedDate?: string | null;

  DurationMinutes?: number | null;
}


export interface DailyProductionStopDetailViewModel extends DailyProductionStopRowViewModel {
  IsOpenStop?: boolean;
}

export interface DailyProductionStopInputDto {
  ProductionId: number | null;
  StopStart: string | null;   // datetime-local value (YYYY-MM-DDTHH:mm)
  StopEnd?: string | null;
  Reason: string | null;
}

export interface DowntimeReasonStatVm {
  Reason: string;
  Minutes: number;
  Percent: number;
  Count: number;
}
