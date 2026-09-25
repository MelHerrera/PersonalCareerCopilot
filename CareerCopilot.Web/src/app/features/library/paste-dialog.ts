import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  computed,
  effect,
  inject,
  signal,
  untracked,
  viewChild,
} from '@angular/core';
import { DocumentKind, KIND_LABELS } from '../../core/api.models';
import { DocumentsStore } from '../../core/documents.store';
import { PasteService } from '../../core/paste.service';
import { Icon } from '../../shared/icon';

const NOUNS: Record<DocumentKind, string> = {
  resume: 'your résumé',
  jobPosting: 'the job posting',
  other: 'a document',
};

/** Treats a single "word" that looks like a web address as a link to import. */
function asUrl(text: string): URL | null {
  const trimmed = text.trim();
  if (!trimmed || /\s/.test(trimmed)) return null;
  const candidate = /^https?:\/\//i.test(trimmed) ? trimmed : /^www\./i.test(trimmed) ? `https://${trimmed}` : null;
  if (!candidate) return null;
  try {
    return new URL(candidate);
  } catch {
    return null;
  }
}

@Component({
  selector: 'app-paste-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Icon],
  templateUrl: './paste-dialog.html',
  styleUrl: './paste-dialog.scss',
})
export class PasteDialog {
  private readonly paste = inject(PasteService);
  private readonly docs = inject(DocumentsStore);
  private readonly dialog = viewChild.required<ElementRef<HTMLDialogElement>>('dialog');
  private readonly textarea = viewChild.required<ElementRef<HTMLTextAreaElement>>('box');

  protected readonly kinds: { value: DocumentKind; label: string }[] = [
    { value: 'jobPosting', label: KIND_LABELS.jobPosting },
    { value: 'resume', label: KIND_LABELS.resume },
    { value: 'other', label: KIND_LABELS.other },
  ];

  protected readonly kind = signal<DocumentKind>('jobPosting');
  protected readonly content = signal('');
  protected readonly title = signal('');
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly url = computed(() => asUrl(this.content()));
  protected readonly noun = computed(() => NOUNS[this.kind()]);
  protected readonly canSubmit = computed(() => !this.busy() && (this.url() !== null || this.content().trim().length > 0));
  protected readonly submitLabel = computed(() => {
    if (this.busy()) return this.url() ? 'Reading the page…' : 'Adding…';
    return this.url() ? 'Import from link' : `Add ${KIND_LABELS[this.kind()].toLowerCase()}`;
  });

  constructor() {
    effect(() => {
      const request = this.paste.request();
      const dialog = this.dialog().nativeElement;
      untracked(() => {
        if (request) {
          this.kind.set(request.kind);
          this.content.set(request.text);
          this.title.set('');
          this.error.set(null);
          if (!dialog.open) dialog.showModal();
          queueMicrotask(() => this.textarea().nativeElement.focus());
        } else if (dialog.open) {
          dialog.close();
        }
      });
    });
  }

  protected async submit() {
    if (!this.canSubmit()) return;
    this.busy.set(true);
    this.error.set(null);

    const url = this.url();
    const problem = url
      ? await this.docs.importUrl(url.href, this.kind())
      : await this.docs.addText(this.content(), this.title(), this.kind());

    this.busy.set(false);
    if (problem) {
      this.error.set(problem);
    } else {
      this.paste.close();
    }
  }

  protected cancel() {
    if (!this.busy()) this.paste.close();
  }

  /** Esc or the backdrop close the native dialog; keep the service in sync. */
  protected onNativeClose() {
    if (this.paste.request()) this.paste.close();
  }

  protected onBackdropClick(event: MouseEvent) {
    if (event.target === this.dialog().nativeElement) this.cancel();
  }
}
