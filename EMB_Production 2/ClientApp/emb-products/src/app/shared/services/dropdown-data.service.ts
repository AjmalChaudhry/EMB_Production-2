import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, of } from 'rxjs';
import { map, catchError, shareReplay } from 'rxjs/operators';

export interface DropdownItem {
  id: number;
  name: string;
}

@Injectable({
  providedIn: 'root'
})
export class DropdownDataService {

  private dropdownUrl = '/emb-products/dropdowns';
  private usersUrl = '/emb-products/users';

  // Cached dropdowns
  private cache: { [key: string]: Observable<any[]> | null } = {
    branches: null,
    companies: null,
    roles: null,
    components: null,
    designs: null,
    brands: null,
    users: null,
    customers: null,
    products: null,
    workorders: null
  };

  constructor(private http: HttpClient) { }

  // --------------------------------------------------------------
  // UNIVERSAL LOADER (Handles {data:[]} or [] formats automatically)
  // --------------------------------------------------------------
  private load<T>(key: string, url: string): Observable<T[]> {

    if (this.cache[key]) {
      return this.cache[key] as Observable<T[]>;
    }

    const obs$ = this.http.get<any>(url).pipe(
      map(res => {
        if (Array.isArray(res)) return res as T[];
        if (res?.data && Array.isArray(res.data)) return res.data as T[];
        return [] as T[];
      }),
      catchError(err => {
        console.error('Dropdown load failed:', key, err);
        return of([] as T[]);
      }),
      shareReplay(1)
    );

    this.cache[key] = obs$;
    return obs$;
  }

  // --------------------------------------------------------------
  // BASIC DROPDOWNS
  // --------------------------------------------------------------
  getBranches(): Observable<DropdownItem[]> {
    return this.load('branches', `${this.dropdownUrl}/branches`);
  }

  getCompanies(): Observable<DropdownItem[]> {
    return this.load('companies', `${this.dropdownUrl}/companies`);
  }

  getRoles(): Observable<DropdownItem[]> {
    return this.load('roles', `${this.usersUrl}/roles`);
  }

  getComponents(): Observable<any[]> {
    return this.load('components', `${this.dropdownUrl}/components`);
  }

  // IMPORTANT: FIXED ROUTE
  getDesigns(): Observable<any[]> {
    return this.load('designs', `${this.dropdownUrl}/designs-for-components`);
  }

  getBrands(): Observable<any[]> {
    return this.load('brands', `${this.dropdownUrl}/brands`);
  }

  getUsers(): Observable<any[]> {
    return this.load('users', `${this.usersUrl}/list`);
  }

  // --------------------------------------------------------------
  // CUSTOM DROPDOWNS
  // --------------------------------------------------------------
  getCustomers(): Observable<DropdownItem[]> {
    return this.load('customers', `${this.dropdownUrl}/customers`);
  }



  getProducts(): Observable<DropdownItem[]> {
    return this.load('products', `${this.dropdownUrl}/products`);
  }

  // --------------------------------------------------------------
  // ✔ NEW: WorkOrders Dropdown
  // --------------------------------------------------------------
  getWorkOrders(): Observable<any[]> {
    return this.load('workorders', `${this.dropdownUrl}/workorders`);
  }

  getMachines(): Observable<any[]> {
    return this.load('machines', `${this.dropdownUrl}/machines`);
  }

  // --------------------------------------------------------------
  // CLEAR CACHE
  // --------------------------------------------------------------
  clearCache(): void {
    Object.keys(this.cache).forEach(key => {
      this.cache[key] = null;
    });
  }
  
}
