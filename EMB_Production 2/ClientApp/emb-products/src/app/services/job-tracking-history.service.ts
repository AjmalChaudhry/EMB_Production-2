import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { JobTrackingHistoryRowViewModel, JobTrackingHistoryDetailViewModel, JobTrackingHistoryDto } from '../shared/models/job-tracking-history.dto';
import { ApiResponseData, ApiPagedResponse, ApiResponse, DropdownItem } from '../shared/models/api-response';

@Injectable({ providedIn: 'root' })

export class JobTrackingHistoryService {
  private baseUrl = '/emb-products/job-tracking-history';

  constructor(private http: HttpClient) { }

  listPaged(workOrderItemId: number, page: number, pageSize: number) {
    return this.http.get<ApiPagedResponse<any>>(
      `${this.baseUrl}/list`,
      {
        params: {
          workOrderItemId: workOrderItemId.toString(),
          page: page.toString(),
          pageSize: pageSize.toString()
        }
      }
    );
  }



  detail(id: number) {
    return this.http.get<ApiResponseData<JobTrackingHistoryDetailViewModel>>(
      `${this.baseUrl}/detail/${id}`
    );
  }

  create(dto: JobTrackingHistoryDto) {
    return this.http.post<ApiResponse>(
      `${this.baseUrl}/create`,
      dto
    );
  }


  update(dto: JobTrackingHistoryDto) {
    return this.http.post<ApiResponse>(`${this.baseUrl}/update`, dto);
  }

  delete(id: number) {
    return this.http.post<ApiResponse>(`${this.baseUrl}/delete/${id}`, {});
  }

  getBranches(): Observable<ApiResponseData<DropdownItem[]>> {
    return this.http.get<ApiResponseData<DropdownItem[]>>(
      '/emb-products/dropdowns/branches'
    );
  }
  getWorkOrderItems(): Observable<{ id: number; label: string }[]> {
    return this.http.get<{ id: number; label: string }[]>(
      '/WorkOrderItems/dropdown'
    );
  }

}
