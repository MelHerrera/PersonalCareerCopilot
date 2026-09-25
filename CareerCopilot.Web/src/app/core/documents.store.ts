import { Injectable, computed, inject, signal } from '@angular/core';
import { describeError } from './api-error';
import { CareerDocument, DocumentKind } from './api.models';
import { DocumentsApi } from './documents.api';
import { ToastService } from './toast.service';

const MAX_UPLOAD_BYTES = 10 * 1024 * 1024;

export interface PendingUpload {
  id: number;
  name: string;
  kind: DocumentKind;
}

@Injectable({ providedIn: 'root' })
export class DocumentsStore {
  private readonly api = inject(DocumentsApi);
  private readonly toast = inject(ToastService);
  private nextUploadId = 0;

  readonly documents = signal<CareerDocument[]>([]);
  readonly loading = signal(false);
  readonly loaded = signal(false);
  readonly loadError = signal<string | null>(null);
  readonly uploads = signal<PendingUpload[]>([]);
  /** Names of documents with an in-flight reindex/delete. */
  readonly busy = signal<ReadonlySet<string>>(new Set());

  readonly indexed = computed(() => this.documents().filter((d) => d.isIndexed));
  readonly needsReindex = computed(() => this.documents().filter((d) => !d.isIndexed));
  readonly hasResume = computed(() => this.indexed().some((d) => d.kind === 'resume'));
  readonly hasJobPosting = computed(() => this.indexed().some((d) => d.kind === 'jobPosting'));

  async refresh() {
    this.loading.set(true);
    try {
      this.documents.set(await this.api.list());
      this.loadError.set(null);
    } catch (error) {
      this.loadError.set(describeError(error));
    } finally {
      this.loading.set(false);
      this.loaded.set(true);
    }
  }

  async upload(files: File[], kind: DocumentKind) {
    const accepted = files.filter((file) => {
      if (!file.name.toLowerCase().endsWith('.pdf')) {
        this.toast.error(`“${file.name}” isn't a PDF. Only PDF files can be added.`);
        return false;
      }
      if (file.size > MAX_UPLOAD_BYTES) {
        this.toast.error(`“${file.name}” is over 10 MB. Try a smaller export of the PDF.`);
        return false;
      }
      return true;
    });
    await Promise.all(accepted.map((file) => this.uploadOne(file, kind)));
  }

  /** Adds pasted text as a document. Resolves to an error message, or null when it worked. */
  addText(text: string, title: string | undefined, kind: DocumentKind): Promise<string | null> {
    const label = title?.trim() || text.trim().split('\n')[0].slice(0, 60);
    return this.track(label, kind, () => this.api.addText(text, title?.trim() || undefined, kind));
  }

  /** Imports a document from a link. Resolves to an error message, or null when it worked. */
  importUrl(url: string, kind: DocumentKind): Promise<string | null> {
    let label = url;
    try {
      label = new URL(url).hostname;
    } catch {
      // Keep the raw text; the API will explain what's wrong with it.
    }
    return this.track(label, kind, () => this.api.importUrl(url, kind));
  }

  /** Most recent indexed-or-not document of a kind, for the desk sheets. */
  latest(kind: DocumentKind): CareerDocument | undefined {
    return this.documents().find((d) => d.kind === kind);
  }

  async reindex(name: string) {
    await this.withBusy(name, async () => {
      const updated = await this.api.reindex(name);
      this.upsert(updated);
      this.toast.success(`Re-indexed “${name}”.`);
    });
  }

  async reindexAll() {
    await Promise.all(this.needsReindex().map((d) => this.reindex(d.name)));
  }

  async remove(name: string) {
    await this.withBusy(name, async () => {
      await this.api.delete(name);
      this.documents.update((list) => list.filter((d) => d.name !== name));
      this.toast.info(`Removed “${name}”.`);
    });
  }

  private async uploadOne(file: File, kind: DocumentKind) {
    const pending: PendingUpload = { id: this.nextUploadId++, name: file.name, kind };
    this.uploads.update((list) => [...list, pending]);
    try {
      const document = await this.api.upload(file, kind);
      this.upsert(document);
      this.toast.success(`“${document.name}” is on the desk and ready to search.`);
    } catch (error) {
      this.toast.error(`Couldn't upload “${file.name}”: ${describeError(error)}`);
    } finally {
      this.uploads.update((list) => list.filter((u) => u.id !== pending.id));
    }
  }

  private async track(label: string, kind: DocumentKind, action: () => Promise<CareerDocument>) {
    const pending: PendingUpload = { id: this.nextUploadId++, name: label, kind };
    this.uploads.update((list) => [...list, pending]);
    try {
      const document = await action();
      this.upsert(document);
      this.toast.success(`“${document.name}” is on the desk and ready to search.`);
      return null;
    } catch (error) {
      return describeError(error);
    } finally {
      this.uploads.update((list) => list.filter((u) => u.id !== pending.id));
    }
  }

  private upsert(document: CareerDocument) {
    this.documents.update((list) => [document, ...list.filter((d) => d.name !== document.name)]);
  }

  private async withBusy(name: string, action: () => Promise<void>) {
    this.busy.update((set) => new Set(set).add(name));
    try {
      await action();
    } catch (error) {
      this.toast.error(describeError(error));
    } finally {
      this.busy.update((set) => {
        const next = new Set(set);
        next.delete(name);
        return next;
      });
    }
  }
}
