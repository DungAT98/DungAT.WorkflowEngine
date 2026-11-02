// Global UI state: toasts and the confirm/prompt dialog (markup lives in index.html).
document.addEventListener('alpine:init', () => {
  let nextToast = 0;

  Alpine.store('ui', {
    toasts: [],
    dialog: null,
    promptValue: '', // bound to the prompt's textarea; kept outside `dialog` so the markup never dereferences null

    toast(message, kind = 'info', ms = 4500) {
      const id = ++nextToast;
      this.toasts.push({ id, message, kind });
      setTimeout(() => this.dismiss(id), ms);
    },
    dismiss(id) {
      this.toasts = this.toasts.filter((t) => t.id !== id);
    },

    confirm({ title, message = '', confirmText = 'Confirm', danger = false }) {
      return new Promise((resolve) => {
        this.dialog = { kind: 'confirm', title, message, confirmText, danger, resolve };
      });
    },
    prompt({ title, label = 'Comment', message = '', confirmText = 'Continue', required = false, danger = false }) {
      return new Promise((resolve) => {
        this.promptValue = '';
        this.dialog = { kind: 'prompt', title, label, message, confirmText, required, danger, resolve };
      });
    },
    submit() {
      const d = this.dialog;
      if (!d) return;
      if (d.kind === 'prompt' && d.required && !this.promptValue.trim()) return;
      this.close(d.kind === 'prompt' ? this.promptValue.trim() : true);
    },
    close(result) {
      const d = this.dialog;
      this.dialog = null;
      d?.resolve(result === null ? (d.kind === 'prompt' ? null : false) : result);
    },
  });
});
