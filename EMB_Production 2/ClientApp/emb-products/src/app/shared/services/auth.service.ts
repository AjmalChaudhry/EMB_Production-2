import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Router } from '@angular/router';
import { environment } from '../../../environments/environment';
import { BehaviorSubject, Observable, tap, catchError, throwError, of, map } from 'rxjs';
import { User, LoginResponse, LogoutResponse } from '../models/auth.dto';

@Injectable({
  providedIn: 'root'
})
export class AuthService {

  private userKey = 'auth_user';
  private baseUrl = `${environment.apiUrl}/emb-products/auth`;

  private loggedInSubject = new BehaviorSubject<boolean>(this.hasUser());
  public isLoggedIn$ = this.loggedInSubject.asObservable();

  constructor(
    private http: HttpClient,
    private router: Router
  ) { }

  // ================= LOGIN =================
  login(email: string, password: string): Observable<LoginResponse> {
    return this.http.post<LoginResponse>(
      `${this.baseUrl}/login`,
      { Email: email, Password: password },
      { withCredentials: true }
    ).pipe(
      tap(res => {
        if (res?.success && res.data) {
          this.saveUser(res.data);
          this.loggedInSubject.next(true);
        }
      }),
      catchError(err => throwError(() => err))
    );
  }

  // ================= LOGOUT =================
  logout(): Observable<LogoutResponse> {
    return this.http.post<LogoutResponse>(
      `${this.baseUrl}/logout`,
      {},
      { withCredentials: true }
    ).pipe(
      tap(() => this.performLocalLogout()),
      catchError(() => {
        this.performLocalLogout();
        return of({ success: true });
      })
    );
  }

  private performLocalLogout(): void {
    this.clearUser();
    this.loggedInSubject.next(false);
    this.router.navigate(['/login']);
  }

  // ================= LOGIN STATE =================
  isLoggedInSync(): boolean {
    return this.loggedInSubject.value;
  }

  private hasUser(): boolean {
    return !!localStorage.getItem(this.userKey);
  }

  // ================= STORAGE =================
  private saveUser(user: User): void {
    localStorage.setItem(this.userKey, JSON.stringify(user));
  }

  private clearUser(): void {
    localStorage.removeItem(this.userKey);
    sessionStorage.removeItem('role');
  }

  getUser(): User | null {
    const data = localStorage.getItem(this.userKey);
    if (!data) return null;

    try {
      return JSON.parse(data);
    } catch {
      this.clearUser();
      return null;
    }
  }

  // ================= USER INFO =================
  getUserName(): string {
    const u = this.getUser();
    return u?.Name || u?.UserName || 'User';
  }

  getUserEmail(): string | null {
    return this.getUser()?.EmailAddress ?? null;
  }

  getUserRole(): number | null {
    return this.getUser()?.RoleId ?? null;
  }

  /** Role name as stored at login time. Empty string when unknown
   *  (e.g. legacy session predating the RoleName payload). The sidebar
   *  treats empty as "show everything" so old logins don't break. */
  getUserRoleName(): string {
    return (this.getUser()?.RoleName || '').trim();
  }

  getUserId(): number | null {
    return this.getUser()?.Id ?? null;
  }

  // ================= ROLE CHECK =================
  hasRole(roleId: number): boolean {
    return this.getUserRole() === roleId;
  }

  hasAnyRole(roles: number[]): boolean {
    const r = this.getUserRole();
    return r ? roles.includes(r) : false;
  }

  // ================= IMAGE =================
  getUserImage(): string {
    const user = this.getUser();
    if (!user?.FacialRecognition) return this.getDefaultUserImage();

    const clean = user.FacialRecognition
      .replace(/^\/?Uploads\/FacialRecognition\//, '')
      .trim();

    return `/Uploads/FacialRecognition/${clean}`;
  }

  private getDefaultUserImage(): string {
    return 'data:image/svg+xml;base64,...';
  }

  // ================= DASHBOARD =================
  getDashboard(): Observable<any> {
    return this.http.get<any>(
      `${this.baseUrl}/dashboard`,
      { withCredentials: true }
    ).pipe(
      map((res: any) => ({
        success: res?.success ?? res?.Success ?? false,
        message: res?.message ?? res?.Message ?? '',
        data: res?.data ?? res?.Data ?? {
          productionSummary: {},
          qcSummary: {},
          stockSummary: {},
          dispatchSummary: {},
          machineSummary: {},
          operatorSummary: {}
        }
      })),
      catchError(err => throwError(() => err))
    );
  }

  // ================= SESSION VALIDATION =================
  validateSession(): Observable<boolean> {
    return of(this.isLoggedInSync());
  }

  refreshUserData(): Observable<LoginResponse> {
    const user = this.getUser();
    if (!user) return throwError(() => new Error('No user found'));
    return of({ success: true, data: user });
  }
}
