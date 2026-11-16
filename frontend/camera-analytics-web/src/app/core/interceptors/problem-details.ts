import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { catchError, throwError } from 'rxjs';

interface ProblemDetails {
  detail?: string;
  title?: string;
  code?: string;
  traceId?: string;
  errors?: Record<string, string[]>;
}

export const problemDetailsInterceptor: HttpInterceptorFn = (request, next) => next(request).pipe(
  catchError((error: HttpErrorResponse) => {
    const problem = typeof error.error === 'object' ? error.error as ProblemDetails | null : null;
    const fields = Object.values(problem?.errors ?? {}).flat().join(' ');
    const text = problem?.detail || fields || problem?.title
      || (error.status === 0 ? 'The analytics API is unavailable. Check that the backend is running.' : 'The request failed.');
    const trace = problem?.traceId ? ` (trace ${problem.traceId})` : '';
    return throwError(() => new Error(text + trace));
  }),
);
