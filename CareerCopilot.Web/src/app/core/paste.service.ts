import { Injectable, signal } from '@angular/core';
import { DocumentKind } from './api.models';

export interface PasteRequest {
  kind: DocumentKind;
  /** Text to start with, e.g. what the user pasted onto the desk. */
  text: string;
}

/** Opens the "paste the job posting" note from anywhere on the desk. */
@Injectable({ providedIn: 'root' })
export class PasteService {
  readonly request = signal<PasteRequest | null>(null);

  open(kind: DocumentKind, text = '') {
    this.request.set({ kind, text });
  }

  close() {
    this.request.set(null);
  }
}
