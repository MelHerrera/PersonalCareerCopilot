import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { HealthStatus } from './api.models';

/** Keeps a check visible long enough to register, even when the API answers instantly. */
const MIN_CHECK_MS = 700;

@Injectable({ providedIn: 'root' })
export class HealthService {
  private readonly http = inject(HttpClient);

  readonly status = signal<HealthStatus | undefined>(undefined);
  readonly unreachable = signal(false);
  readonly checking = signal(false);
  /** True when the user asked to check again and the problem is still there. */
  readonly stillFailing = signal(false);

  /** A short description of what is blocking the app from working, if anything. */
  readonly problem = computed<{ title: string; detail: string } | null>(() => {
    if (this.unreachable()) {
      return {
        title: "Can't reach the Career Copilot API",
        detail: 'Start it with dotnet run in CareerCopilot.API, then check again.',
      };
    }
    const health = this.status();
    if (health && !health.azureOpenAIConfigured) {
      return {
        title: "Azure OpenAI isn't connected yet",
        detail: 'Add your endpoint and API key to the API settings to index documents and get answers.',
      };
    }
    return null;
  });

  constructor() {
    this.fetch();
  }

  /** Re-checks on request, showing progress and reporting if nothing changed. */
  async checkAgain() {
    if (this.checking()) return;
    this.checking.set(true);
    this.stillFailing.set(false);
    await Promise.all([this.fetch(), new Promise((resolve) => setTimeout(resolve, MIN_CHECK_MS))]);
    this.checking.set(false);
    this.stillFailing.set(this.problem() !== null);
  }

  private async fetch() {
    try {
      this.status.set(await firstValueFrom(this.http.get<HealthStatus>('/api/health')));
      this.unreachable.set(false);
    } catch {
      this.unreachable.set(true);
    }
  }
}
