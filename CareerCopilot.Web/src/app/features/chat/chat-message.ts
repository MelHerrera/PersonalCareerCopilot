import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  afterRenderEffect,
  input,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { ChatMessage } from '../../core/chat.store';
import { MarkdownPipe } from '../../shared/pipes';

@Component({
  selector: 'app-chat-message',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MarkdownPipe],
  templateUrl: './chat-message.html',
  styleUrl: './chat-message.scss',
  host: { '[class]': 'message().role' },
})
export class ChatMessageView {
  readonly message = input.required<ChatMessage>();
  readonly retry = output<void>();

  protected readonly openSource = signal<string | null>(null);
  protected readonly copied = signal(false);

  private readonly prose = viewChild<ElementRef<HTMLElement>>('prose');
  private sawStreaming = false;

  constructor() {
    // When an answer finishes arriving, the copilot marks up its key phrases one
    // after another. Answers restored from a previous visit stay still.
    afterRenderEffect(() => {
      const status = this.message().status;
      if (status === 'streaming') {
        this.sawStreaming = true;
        return;
      }
      const el = this.prose()?.nativeElement;
      if (status === 'done' && this.sawStreaming && el) {
        this.sawStreaming = false;
        el.querySelectorAll('strong').forEach((strong, i) => strong.style.setProperty('--i', `${Math.min(i, 12)}`));
        el.classList.add('marking');
      }
    });
  }

  protected toggleSource(fileName: string) {
    this.openSource.update((current) => (current === fileName ? null : fileName));
  }

  protected async copy() {
    try {
      await navigator.clipboard.writeText(this.message().content);
      this.copied.set(true);
      setTimeout(() => this.copied.set(false), 1600);
    } catch {
      // Clipboard can be blocked (insecure context / permissions); nothing to do.
    }
  }
}
