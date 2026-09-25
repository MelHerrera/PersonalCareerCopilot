import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { CareerDocument, DocumentKind } from './api.models';

const BASE = '/api/documents';

@Injectable({ providedIn: 'root' })
export class DocumentsApi {
  private readonly http = inject(HttpClient);

  list(): Promise<CareerDocument[]> {
    return firstValueFrom(this.http.get<CareerDocument[]>(BASE));
  }

  upload(file: File, kind: DocumentKind): Promise<CareerDocument> {
    const form = new FormData();
    form.append('file', file);
    form.append('kind', kind);
    return firstValueFrom(this.http.post<CareerDocument>(BASE, form));
  }

  addText(text: string, title: string | undefined, kind: DocumentKind): Promise<CareerDocument> {
    return firstValueFrom(this.http.post<CareerDocument>(`${BASE}/text`, { text, title, kind }));
  }

  importUrl(url: string, kind: DocumentKind): Promise<CareerDocument> {
    return firstValueFrom(this.http.post<CareerDocument>(`${BASE}/import`, { url, kind }));
  }

  reindex(name: string): Promise<CareerDocument> {
    return firstValueFrom(this.http.post<CareerDocument>(`${BASE}/${encodeURIComponent(name)}/reindex`, null));
  }

  delete(name: string): Promise<void> {
    return firstValueFrom(this.http.delete<void>(`${BASE}/${encodeURIComponent(name)}`));
  }

  fileUrl(name: string): string {
    return `${BASE}/${encodeURIComponent(name)}/file`;
  }
}
