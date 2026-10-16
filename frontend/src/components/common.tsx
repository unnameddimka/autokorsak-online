import {
  createContext, useCallback, useContext, useEffect, useId, useRef, useState, useSyncExternalStore, type ReactNode,
} from 'react';
import { saveStatus } from '../api/client';
import { LOCALES, useI18n, type Locale } from '../i18n';
import type { MessageKey } from '../i18n/en';
import { errorMessage } from '../format';

// ---------- modal ----------

export function Modal({ title, onClose, children, footer, wide }: {
  title: string;
  onClose: () => void;
  children: ReactNode;
  footer?: ReactNode;
  wide?: boolean;
}) {
  const ref = useRef<HTMLDialogElement>(null);
  const titleId = useId();
  const { t } = useI18n();
  useEffect(() => {
    const dialog = ref.current;
    if (dialog && !dialog.open) dialog.showModal();
  }, []);
  return (
    <dialog
      ref={ref}
      className={wide ? 'modal modal-wide' : 'modal'}
      aria-labelledby={titleId}
      onCancel={(e) => { e.preventDefault(); onClose(); }}
    >
      <header className="modal-header">
        <h2 id={titleId}>{title}</h2>
        <button type="button" className="icon-button" onClick={onClose} aria-label={t('common.close')}>×</button>
      </header>
      <div className="modal-body">{children}</div>
      {footer && <footer className="modal-footer">{footer}</footer>}
    </dialog>
  );
}

// ---------- confirmation ----------

interface ConfirmOptions { title: string; message: string; confirmLabel?: string; danger?: boolean }
type ConfirmFn = (options: ConfirmOptions) => Promise<boolean>;
const ConfirmContext = createContext<ConfirmFn | null>(null);

export function ConfirmProvider({ children }: { children: ReactNode }) {
  const { t } = useI18n();
  const [state, setState] = useState<(ConfirmOptions & { resolve: (v: boolean) => void }) | null>(null);
  const confirm = useCallback<ConfirmFn>((options) => new Promise((resolve) => setState({ ...options, resolve })), []);
  const close = (value: boolean) => { state?.resolve(value); setState(null); };
  return (
    <ConfirmContext.Provider value={confirm}>
      {children}
      {state && (
        <Modal
          title={state.title}
          onClose={() => close(false)}
          footer={
            <>
              <button type="button" onClick={() => close(false)}>{t('common.cancel')}</button>
              <button type="button" className={state.danger ? 'danger' : 'primary'} onClick={() => close(true)} autoFocus>
                {state.confirmLabel ?? t('common.confirm')}
              </button>
            </>
          }
        >
          <p className="confirm-message">{state.message}</p>
        </Modal>
      )}
    </ConfirmContext.Provider>
  );
}

export function useConfirm(): ConfirmFn {
  const ctx = useContext(ConfirmContext);
  if (!ctx) throw new Error('useConfirm must be used inside ConfirmProvider');
  return ctx;
}

// ---------- toasts ----------

interface Toast { id: number; kind: 'error' | 'success' | 'warning'; text: string }
interface ToastApi { error: (error: unknown) => void; success: (text: string) => void; warning: (text: string) => void }
const ToastContext = createContext<ToastApi | null>(null);

export function ToastProvider({ children }: { children: ReactNode }) {
  const { tk, t } = useI18n();
  const [toasts, setToasts] = useState<Toast[]>([]);
  const push = useCallback((kind: Toast['kind'], text: string) => {
    const id = Date.now() + Math.random();
    setToasts((list) => [...list, { id, kind, text }]);
    setTimeout(() => setToasts((list) => list.filter((x) => x.id !== id)), kind === 'success' ? 3500 : 9000);
  }, []);
  const apiValue = useRef<ToastApi>({ error: () => {}, success: () => {}, warning: () => {} });
  apiValue.current.error = (error) => push('error', errorMessage(error, tk));
  apiValue.current.success = (text) => push('success', text);
  apiValue.current.warning = (text) => push('warning', text);
  const stable = useRef<ToastApi>({
    error: (e) => apiValue.current.error(e),
    success: (s) => apiValue.current.success(s),
    warning: (s) => apiValue.current.warning(s),
  });
  return (
    <ToastContext.Provider value={stable.current}>
      {children}
      <div className="toasts" role="status" aria-live="polite">
        {toasts.map((toast) => (
          <div key={toast.id} className={`toast toast-${toast.kind}`}>
            <span>{toast.text}</span>
            <button type="button" className="icon-button" aria-label={t('common.close')}
              onClick={() => setToasts((l) => l.filter((x) => x.id !== toast.id))}>×</button>
          </div>
        ))}
      </div>
    </ToastContext.Provider>
  );
}

export function useToast(): ToastApi {
  const ctx = useContext(ToastContext);
  if (!ctx) throw new Error('useToast must be used inside ToastProvider');
  return ctx;
}

// ---------- small pieces ----------

export function LanguageSelect() {
  const { locale, setLocale, t } = useI18n();
  const names: Record<Locale, MessageKey> = { uk: 'language.uk', en: 'language.en' };
  return (
    <label className="language-select">
      <span className="visually-hidden">{t('language.label')}</span>
      <select value={locale} onChange={(e) => setLocale(e.target.value as Locale)} aria-label={t('language.label')}>
        {LOCALES.map((l) => <option key={l} value={l}>{t(names[l])}</option>)}
      </select>
    </label>
  );
}

export function SaveIndicator() {
  const { t } = useI18n();
  const state = useSyncExternalStore(saveStatus.subscribe, saveStatus.get);
  if (state.pending > 0) return <span className="save-status saving">{t('save.saving')}</span>;
  if (state.failed) return <span className="save-status failed">{t('save.failed')}</span>;
  if (state.lastSavedAt) return <span className="save-status saved">{t('save.saved')}</span>;
  return null;
}

export function Notice({ kind = 'info', children }: { kind?: 'info' | 'warning' | 'error'; children: ReactNode }) {
  return <div className={`notice notice-${kind}`} role={kind === 'error' ? 'alert' : undefined}>{children}</div>;
}

/** Marks a setting or control that is stored or shown but not implemented in this version. */
export function NotImplementedBadge({ label }: { label?: string }) {
  const { t } = useI18n();
  return <span className="badge badge-muted" title={t('common.notImplementedHint')}>{label ?? t('common.notImplemented')}</span>;
}

export function Checkbox({ label, checked, onChange, disabled, hint }: {
  label: ReactNode; checked: boolean; onChange: (v: boolean) => void; disabled?: boolean; hint?: ReactNode;
}) {
  return (
    <label className={`checkbox${disabled ? ' disabled' : ''}`}>
      <input type="checkbox" checked={checked} disabled={disabled} onChange={(e) => onChange(e.target.checked)} />
      <span>{label}{hint && <span className="hint-inline">{hint}</span>}</span>
    </label>
  );
}

export function Field({ label, children, hint, wide }: { label: ReactNode; children: ReactNode; hint?: ReactNode; wide?: boolean }) {
  return (
    <label className={`field${wide ? ' field-wide' : ''}`}>
      <span className="field-label">{label}</span>
      {children}
      {hint && <span className="field-hint">{hint}</span>}
    </label>
  );
}
