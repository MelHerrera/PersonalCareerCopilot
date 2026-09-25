import { ChangeDetectionStrategy, Component, inject, signal, viewChild } from '@angular/core';
import { ChatStore } from './core/chat.store';
import { DocumentsStore } from './core/documents.store';
import { HealthService } from './core/health';
import { PasteService } from './core/paste.service';
import { ThemeService } from './core/theme.service';
import { ToastService } from './core/toast.service';
import { ChatView } from './features/chat/chat-view';
import { LibraryPanel } from './features/library/library-panel';
import { PasteDialog } from './features/library/paste-dialog';
import { Icon } from './shared/icon';

@Component({
  selector: 'app-root',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Icon, LibraryPanel, ChatView, PasteDialog],
  templateUrl: './app.html',
  styleUrl: './app.scss',
  host: {
    '(document:keydown.escape)': 'sidebarOpen.set(false)',
    '(document:paste)': 'onPaste($event)',
  },
})
export class App {
  protected readonly chat = inject(ChatStore);
  protected readonly docs = inject(DocumentsStore);
  protected readonly health = inject(HealthService);
  protected readonly theme = inject(ThemeService);
  protected readonly toasts = inject(ToastService);
  private readonly paste = inject(PasteService);

  /** Library drawer visibility on narrow screens. */
  protected readonly sidebarOpen = signal(false);
  private readonly chatView = viewChild.required(ChatView);

  constructor() {
    this.docs.refresh();
  }

  /** The setup note's button: re-check the API and retry loading the documents. */
  protected checkAgain() {
    this.health.checkAgain();
    this.docs.refresh();
  }

  /**
   * Pasting anywhere on the desk (outside a text field) opens the paste note with
   * that text, so a copied posting or link goes straight in.
   */
  protected onPaste(event: ClipboardEvent) {
    const target = event.target as HTMLElement | null;
    if (target?.closest('input, textarea, [contenteditable], dialog')) return;

    const text = event.clipboardData?.getData('text/plain')?.trim();
    if (!text) return;

    event.preventDefault();
    this.paste.open('jobPosting', text);
  }

  protected newChat() {
    this.chat.clear();
    this.chatView().focusInput();
  }
}
