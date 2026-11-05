import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable, throwError } from 'rxjs';
import { catchError, map } from 'rxjs/operators';

import {
  DesignDetailViewModel,
  DesignListViewModel
} from '../shared/models/design.dto';

import {
  ApiPagedResponse,
  ApiResponseData,
  DropdownItem
} from '../shared/models/api-response';


@Injectable({
  providedIn: 'root'
})
export class DesignService {

  private baseUrl = '/emb-products/gen_design';
  private dropdownUrl = '/emb-products/dropdowns';

  constructor(private http: HttpClient) { }


  // ================= LIST =================
  getAll(
    search: string,
    page: number,
    pageSize: number,
    activeFilter: string,
    extras?: {
      productFilter?: number | null;
      machineFilter?: number | null;
      customerFilter?: number | null;
      branchFilter?: number | null;
      sortBy?: string;
    }
  ): Observable<ApiPagedResponse<DesignListViewModel>> {

    let params = new HttpParams()
      .set('search', search || '')
      .set('page', page)
      .set('pageSize', pageSize)
      .set('activeFilter', activeFilter || '');

    if (extras?.productFilter)  params = params.set('productFilter',  extras.productFilter);
    if (extras?.machineFilter)  params = params.set('machineFilter',  extras.machineFilter);
    if (extras?.customerFilter) params = params.set('customerFilter', extras.customerFilter);
    if (extras?.branchFilter)   params = params.set('branchFilter',   extras.branchFilter);
    if (extras?.sortBy)         params = params.set('sortBy',         extras.sortBy);

    return this.http.get<ApiPagedResponse<DesignListViewModel>>(
      `${this.baseUrl}/list`,
      { params }
    );
  }

  // ================= CLONE =================
  clone(id: number): Observable<ApiResponseData<any>> {
    return this.http.post<ApiResponseData<any>>(
      `${this.baseUrl}/clone/${id}`,
      {}
    );
  }

  // ================= PROFILE (design + components) =================
  getProfile(id: number): Observable<ApiResponseData<any>> {
    return this.http.get<ApiResponseData<any>>(
      `${this.baseUrl}/profile/${id}`
    );
  }

  saveComponent(payload: {
    id?: number;
    name: string;
    designId: number;
    headToHead?: number;
    stitch?: number;
    tilla?: number;
    seqA?: number;
    seqB?: number;
    width?: number | null;
    height?: number | null;
    colors?: number | null;
    machineHead?: number | null;
    repeat?: number | null;
    hSpace?: number | null;
    ups?: number | null;
    branchId: number;
    active: boolean;
  }): Observable<ApiResponseData<any>> {
    const isUpdate = !!(payload.id && payload.id > 0);
    const url = isUpdate
      ? '/emb-products/components/update'
      : '/emb-products/components/create';
    return this.http.post<ApiResponseData<any>>(url, payload);
  }

  deleteComponent(id: number): Observable<ApiResponseData<any>> {
    return this.http.delete<ApiResponseData<any>>(
      `/emb-products/components/delete/${id}`
    );
  }

  // ================= FILES (Phase 2A) =================
  uploadFile(designId: number, file: File): Observable<ApiResponseData<any>> {
    const fd = new FormData();
    fd.append('file', file, file.name);
    return this.http.post<ApiResponseData<any>>(
      `${this.baseUrl}/files/upload/${designId}`,
      fd
    );
  }

  listFiles(designId: number): Observable<ApiResponseData<any[]>> {
    return this.http.get<ApiResponseData<any[]>>(
      `${this.baseUrl}/files/list/${designId}`
    );
  }

  downloadFileUrl(fileId: number): string {
    return `${this.baseUrl}/files/download/${fileId}`;
  }

  deleteFile(fileId: number): Observable<ApiResponseData<any>> {
    return this.http.delete<ApiResponseData<any>>(
      `${this.baseUrl}/files/${fileId}`
    );
  }

  setPrimaryFile(fileId: number): Observable<ApiResponseData<any>> {
    return this.http.post<ApiResponseData<any>>(
      `${this.baseUrl}/files/set-primary/${fileId}`,
      {}
    );
  }

  // ================= THREAD USAGE (Phase 2B) =================
  getThreadUsage(designId: number): Observable<ApiResponseData<any>> {
    return this.http.get<ApiResponseData<any>>(
      `${this.baseUrl}/thread-usage/${designId}`
    );
  }

  // ================= VERSIONING + COMPARISON (Phase 3) =================
  snapshotNewVersion(componentId: number): Observable<ApiResponseData<any>> {
    return this.http.post<ApiResponseData<any>>(
      `${this.baseUrl}/components/snapshot-version/${componentId}`,
      {}
    );
  }

  captureOriginal(componentId: number): Observable<ApiResponseData<any>> {
    return this.http.post<ApiResponseData<any>>(
      `${this.baseUrl}/components/capture-original/${componentId}`,
      {}
    );
  }

  componentVersions(componentId: number): Observable<ApiResponseData<any[]>> {
    return this.http.get<ApiResponseData<any[]>>(
      `${this.baseUrl}/components/versions/${componentId}`
    );
  }

  compareDesign(designId: number): Observable<ApiResponseData<any>> {
    return this.http.get<ApiResponseData<any>>(
      `${this.baseUrl}/compare/${designId}`
    );
  }


  // ================= DETAIL =================
  getDetail(id: number): Observable<ApiResponseData<DesignDetailViewModel>> {

    return this.http.get<ApiResponseData<DesignDetailViewModel>>(
      `${this.baseUrl}/detail/${id}`
    );

  }


  // ================= CREATE =================
  create(model: {

    dNo: string;
    collectionName: string;

    productId: number | null;
    machineId: number | null;
    customerId: number | null;

    active: boolean;

  }): Observable<ApiResponseData<any>> {

    const payload = {

      dNo: model.dNo?.trim(),
      collectionName: model.collectionName?.trim(),

      productId: model.productId != null
        ? Number(model.productId)
        : null,

      machineId: model.machineId != null
        ? Number(model.machineId)
        : null,

      customerId: model.customerId != null
        ? Number(model.customerId)
        : null,

      active: model.active ?? true

    };

    return this.http.post<ApiResponseData<any>>(
      `${this.baseUrl}/create`,
      payload
    ).pipe(

      catchError(err => {
        console.error('❌ Design create API error', err);
        return throwError(() => err);
      })

    );

  }


  // ================= UPDATE =================
  update(model: {

    id: number;

    dNo: string;
    collectionName: string;

    productId: number | null;
    machineId: number | null;
    customerId: number | null;

    active: boolean;

  }): Observable<ApiResponseData<any>> {

    const payload = {

      id: model.id,

      dNo: model.dNo?.trim(),
      collectionName: model.collectionName?.trim(),

      productId: model.productId != null
        ? Number(model.productId)
        : null,

      machineId: model.machineId != null
        ? Number(model.machineId)
        : null,

      customerId: model.customerId != null
        ? Number(model.customerId)
        : null,

      active: model.active ?? true

    };

    return this.http.post<ApiResponseData<any>>(
      `${this.baseUrl}/update`,
      payload
    );

  }


  // ================= DELETE =================
  delete(id: number): Observable<ApiResponseData<any>> {

    return this.http.post<ApiResponseData<any>>(
      `${this.baseUrl}/delete`,
      { id }
    );

  }


  // ================= DROPDOWNS =================

  getCustomers(): Observable<ApiResponseData<DropdownItem[]>> {

    return this.http.get<ApiResponseData<DropdownItem[]>>(
      `${this.dropdownUrl}/customers`
    );

  }


  getBranches(): Observable<ApiResponseData<DropdownItem[]>> {

    return this.http.get<ApiResponseData<DropdownItem[]>>(
      `${this.dropdownUrl}/branches`
    );

  }


  getProducts(): Observable<ApiResponseData<DropdownItem[]>> {

    return this.http.get<ApiResponseData<DropdownItem[]>>(
      `${this.dropdownUrl}/products`
    );

  }


  getMachines(): Observable<ApiResponseData<DropdownItem[]>> {

    return this.http.get<ApiResponseData<DropdownItem[]>>(
      `${this.dropdownUrl}/machines`
    );

  }

}
