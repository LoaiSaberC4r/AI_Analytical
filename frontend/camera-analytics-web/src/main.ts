import { bootstrapApplication } from '@angular/platform-browser';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { App } from './app/app';
import { routes } from './app/routes';
import { loadConfiguration, CONFIGURATION } from './app/core/configuration/configuration';
import { problemDetailsInterceptor } from './app/core/interceptors/problem-details';

loadConfiguration().then(configuration => bootstrapApplication(App, {
  providers: [
    { provide: CONFIGURATION, useValue: configuration },
    provideRouter(routes, withComponentInputBinding()),
    provideHttpClient(withInterceptors([problemDetailsInterceptor])),
  ],
})).catch(error => {
  document.querySelector('ca-root')!.textContent = error instanceof Error ? error.message : 'Application startup failed.';
});
