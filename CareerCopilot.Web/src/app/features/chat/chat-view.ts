import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  afterRenderEffect,
  computed,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { DocumentKind } from '../../core/api.models';
import { ChatStore } from '../../core/chat.store';
import { DocumentsStore } from '../../core/documents.store';
import { PasteService } from '../../core/paste.service';
import { ChatMessageView } from './chat-message';

/** One line of the sketched page drawn on each desk sheet. */
interface SketchLine {
  width: number;
  style?: 'title' | 'heading' | 'bullet';
  /** Gets highlighted once a real document is on the sheet. */
  marked?: boolean;
}

interface DeskSheet {
  kind: DocumentKind;
  title: string;
  lines: SketchLine[];
}

@Component({
  selector: 'app-chat-view',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ChatMessageView],
  templateUrl: './chat-view.html',
  styleUrl: './chat-view.scss',
})
export class ChatView {
  protected readonly chat = inject(ChatStore);
  protected readonly docs = inject(DocumentsStore);
  protected readonly paste = inject(PasteService);

  private readonly thread = viewChild<ElementRef<HTMLElement>>('thread');
  private readonly input = viewChild.required<ElementRef<HTMLTextAreaElement>>('input');

  protected readonly draft = signal('');
  protected readonly canSend = computed(() => this.draft().trim().length > 0 && !this.chat.isStreaming());
  /** Sheet currently being dragged over, so it can lift off the desk. */
  protected readonly dragOver = signal<DocumentKind | null>(null);
  protected readonly deskIsEmpty = computed(() => !this.docs.latest('resume') && !this.docs.latest('jobPosting'));
  /** Follow new tokens only while the user hasn't scrolled up to read. */
  private pinnedToBottom = true;

  protected readonly sheets: DeskSheet[] = [
    {
      kind: 'resume',
      title: 'Your résumé',
      lines: [
        { width: 34, style: 'heading' },
        { width: 92, marked: true },
        { width: 80 },
        { width: 86, marked: true },
        { width: 34, style: 'heading' },
        { width: 74 },
        { width: 90, marked: true },
        { width: 58 },
      ],
    },
    {
      kind: 'jobPosting',
      title: 'The job posting',
      lines: [
        { width: 78, style: 'title' },
        { width: 46 },
        { width: 40, style: 'heading' },
        { width: 84, style: 'bullet', marked: true },
        { width: 70, style: 'bullet' },
        { width: 88, style: 'bullet', marked: true },
        { width: 62, style: 'bullet' },
        { width: 76, style: 'bullet', marked: true },
      ],
    },
  ];

  protected readonly starters = [
    'How well does my résumé match this job posting?',
    'What does the posting ask for that my résumé doesn’t show?',
    'Write the opening of a cover letter for this role.',
    'Which interview questions should I prepare for?',
  ];

  constructor() {
    // Keep the newest words in view while an answer streams in.
    afterRenderEffect(() => {
      this.chat.messages();
      if (this.chat.isEmpty()) return;
      const el = this.thread()?.nativeElement;
      if (el && this.pinnedToBottom) el.scrollTop = el.scrollHeight;
    });
  }

  protected uploadingKind(kind: DocumentKind) {
    return this.docs.uploads().some((u) => u.kind === kind);
  }

  protected onSheetDragOver(event: DragEvent, kind: DocumentKind) {
    event.preventDefault();
    this.dragOver.set(kind);
  }

  protected onSheetDrop(event: DragEvent, kind: DocumentKind) {
    event.preventDefault();
    this.dragOver.set(null);
    this.docs.upload(Array.from(event.dataTransfer?.files ?? []).slice(0, 1), kind);
  }

  /** Clicking the paper itself (not one of its buttons) opens the file picker. */
  protected onSheetClick(event: MouseEvent, picker: HTMLInputElement) {
    if ((event.target as HTMLElement).closest('button, a, input')) return;
    picker.click();
  }

  protected onSheetPicked(input: HTMLInputElement, kind: DocumentKind) {
    this.docs.upload(Array.from(input.files ?? []).slice(0, 1), kind);
    input.value = '';
  }

  protected onScroll(el: HTMLElement) {
    this.pinnedToBottom = el.scrollHeight - el.scrollTop - el.clientHeight < 80;
  }

  protected onKeydown(event: KeyboardEvent) {
    if (event.key === 'Enter' && !event.shiftKey && !event.isComposing) {
      event.preventDefault();
      this.submit();
    }
  }

  protected submit(text = this.draft()) {
    if (!text.trim() || this.chat.isStreaming()) return;
    this.pinnedToBottom = true;
    this.draft.set('');
    this.chat.send(text);
    this.input().nativeElement.focus();
  }

  focusInput() {
    this.input().nativeElement.focus();
  }
}
