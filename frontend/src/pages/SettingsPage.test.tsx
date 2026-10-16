// @vitest-environment jsdom
import { afterEach, beforeAll, beforeEach, describe, expect, it } from 'vitest';
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { I18nProvider } from '../i18n';
import { ConfirmProvider, ToastProvider } from '../components/common';
import { NewTournamentPage } from './SettingsPage';

beforeAll(() => {
  // Data routers build fetch Requests; Node's Request rejects jsdom's AbortSignal, so drop the signal in tests.
  const NativeRequest = globalThis.Request;
  globalThis.Request = class extends NativeRequest {
    constructor(input: RequestInfo | URL, init?: RequestInit) {
      const { signal: _signal, ...rest } = init ?? {};
      super(input, rest);
    }
  } as typeof Request;
  // jsdom does not implement modal dialogs.
  HTMLDialogElement.prototype.showModal = function showModal(this: HTMLDialogElement) { this.setAttribute('open', ''); };
  HTMLDialogElement.prototype.close = function close(this: HTMLDialogElement) { this.removeAttribute('open'); };
});

beforeEach(() => localStorage.setItem('autokorsak.locale', 'en'));
afterEach(cleanup);

function renderNewTournamentPage() {
  const router = createMemoryRouter(
    [
      { path: '/new', element: <NewTournamentPage /> },
      { path: '/', element: <p>tournament list</p> },
    ],
    { initialEntries: ['/new'] },
  );
  const view = render(
    <I18nProvider>
      <ToastProvider>
        <ConfirmProvider>
          <RouterProvider router={router} />
        </ConfirmProvider>
      </ToastProvider>
    </I18nProvider>,
  );
  const formCancel = () => within(view.container.querySelector('form.settings')!.parentElement!)
    .getAllByRole('button', { name: 'Cancel' }).find((b) => !b.closest('dialog'))!;
  const openDialog = () => view.container.ownerDocument.querySelector('dialog[open]') as HTMLElement | null;
  return { router, view, formCancel, openDialog };
}

describe('settings form navigation guard', () => {
  it('leaves without a prompt when there are no unsaved changes', async () => {
    const { router, formCancel, openDialog } = renderNewTournamentPage();
    fireEvent.click(formCancel());
    await screen.findByText('tournament list');
    expect(router.state.location.pathname).toBe('/');
    expect(openDialog()).toBeNull();
  });

  it('asks before discarding unsaved settings on in-app navigation', async () => {
    const { router, view, formCancel, openDialog } = renderNewTournamentPage();
    const name = view.container.querySelector('input[required]') as HTMLInputElement;
    fireEvent.change(name, { target: { value: 'Unsaved cup' } });

    // Stay: the edit survives.
    fireEvent.click(formCancel());
    await waitFor(() => expect(openDialog()).not.toBeNull());
    expect(within(openDialog()!).getByText('Discard unsaved settings?')).toBeTruthy();
    fireEvent.click(within(openDialog()!).getByRole('button', { name: 'Cancel' }));
    await waitFor(() => expect(openDialog()).toBeNull());
    expect(router.state.location.pathname).toBe('/new');
    expect((view.container.querySelector('input[required]') as HTMLInputElement).value).toBe('Unsaved cup');

    // Leave: navigation proceeds.
    fireEvent.click(formCancel());
    await waitFor(() => expect(openDialog()).not.toBeNull());
    fireEvent.click(within(openDialog()!).getByRole('button', { name: 'Discard and leave' }));
    await screen.findByText('tournament list');
    expect(router.state.location.pathname).toBe('/');
  });
});
