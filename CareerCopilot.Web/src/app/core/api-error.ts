import { HttpErrorResponse } from '@angular/common/http';
import { ProblemDetails } from './api.models';

export class ApiError extends Error {
  constructor(
    message: string,
    readonly status?: number,
  ) {
    super(message);
  }
}

/** Turns a ProblemDetails body (or any thrown value) into a message fit for the UI. */
export function describeError(error: unknown): string {
  if (error instanceof ApiError) return error.message;

  if (error instanceof HttpErrorResponse) {
    if (error.status === 0) return "Can't reach the CareerCopilot API. Is it running?";
    return messageFromProblem(error.error) ?? `Request failed (${error.status}).`;
  }

  if (error instanceof Error) return error.message;
  return 'Something went wrong.';
}

export function messageFromProblem(body: unknown): string | undefined {
  if (!body || typeof body !== 'object') return typeof body === 'string' && body ? body : undefined;

  const problem = body as ProblemDetails;
  const firstFieldError = problem.errors ? Object.values(problem.errors).flat()[0] : undefined;
  if (firstFieldError) return firstFieldError;
  if (problem.title && problem.detail) return `${problem.title}. ${problem.detail}`;
  return problem.title ?? problem.detail;
}
