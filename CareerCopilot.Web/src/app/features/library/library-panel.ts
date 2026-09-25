import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { CareerDocument, DocumentKind, KIND_LABELS } from '../../core/api.models';
import { DocumentsApi } from '../../core/documents.api';
import { DocumentsStore } from '../../core/documents.store';
import { PasteService } from '../../core/paste.service';
import { Icon } from '../../shared/icon';
import { FileSizePipe, TimeAgoPipe } from '../../shared/pipes';

@Component({
  selector: 'app-library-panel',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Icon, FileSizePipe, TimeAgoPipe],
  templateUrl: './library-panel.html',
  styleUrl: './library-panel.scss',
})
export class LibraryPanel {
  protected readonly store = inject(DocumentsStore);
  protected readonly api = inject(DocumentsApi);
  protected readonly paste = inject(PasteService);

  protected readonly kinds: { value: DocumentKind; label: string }[] = [
    { value: 'resume', label: KIND_LABELS.resume },
    { value: 'jobPosting', label: KIND_LABELS.jobPosting },
    { value: 'other', label: KIND_LABELS.other },
  ];
  protected readonly kindLabels = KIND_LABELS;

  protected readonly selectedKind = signal<DocumentKind>('resume');
  protected readonly dragging = signal(false);
  /** Document awaiting a second click to confirm deletion. */
  protected readonly confirmingDelete = signal<string | null>(null);

  protected hostOf(url: string) {
    try {
      return new URL(url).hostname.replace(/^www\./, '');
    } catch {
      return url;
    }
  }

  protected onFilesPicked(input: HTMLInputElement) {
    this.store.upload(Array.from(input.files ?? []), this.selectedKind());
    input.value = '';
  }

  protected onDragOver(event: DragEvent) {
    event.preventDefault();
    this.dragging.set(true);
  }

  protected onDrop(event: DragEvent) {
    event.preventDefault();
    this.dragging.set(false);
    this.store.upload(Array.from(event.dataTransfer?.files ?? []), this.selectedKind());
  }

  protected requestDelete(doc: CareerDocument) {
    if (this.confirmingDelete() === doc.name) {
      this.confirmingDelete.set(null);
      this.store.remove(doc.name);
      return;
    }
    this.confirmingDelete.set(doc.name);
    setTimeout(() => {
      if (this.confirmingDelete() === doc.name) this.confirmingDelete.set(null);
    }, 3500);
  }
}
