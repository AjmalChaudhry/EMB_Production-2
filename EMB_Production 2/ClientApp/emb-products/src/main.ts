import { bootstrapApplication } from '@angular/platform-browser';
import { AppComponent } from './app/app.component';
import { routes } from './app/app.routes';
import { provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';

bootstrapApplication(AppComponent, {
  providers: [
    // ✅ Modern HTTP client (preferred in Angular 20)
    provideHttpClient(),

    // ✅ Router
    provideRouter(routes)

    // ❌ Removed importProvidersFrom - not needed for CommonModule in standalone
  ]
}).catch(err => console.error(err));
