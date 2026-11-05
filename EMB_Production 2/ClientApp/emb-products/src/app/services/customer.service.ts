import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';   // ✅ add this

// DTO for Customer
export interface CustomerDto {
  id: number;
  name: string;
  email: string;
  phone?: string;
}

export interface CustomerApiResponse {
  items: CustomerDto[];
  total: number;
}
@Injectable({
  providedIn: 'root'
})
export class AuthService {
  private userKey = 'auth_user';

  constructor(private http: HttpClient) { }

  login(email: string, password: string) {
    return this.http.post<any>('/emb-products/auth/login', { email, password }, { withCredentials: true });
  }

  logout() {
    return this.http.post<any>('/emb-products/auth/logout', {}, { withCredentials: true });
  }

  saveUser(user: any) {
    localStorage.setItem(this.userKey, JSON.stringify(user));
  }

  getUser(): any {
    const stored = localStorage.getItem(this.userKey);
    return stored ? JSON.parse(stored) : null;
  }

  clearUser() {
    localStorage.removeItem(this.userKey);
  }

  isLoggedIn(): boolean {
    return this.getUser() != null;
  }
}
