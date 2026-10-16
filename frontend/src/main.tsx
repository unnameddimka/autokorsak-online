import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { createBrowserRouter, Link, Navigate, Outlet, RouterProvider } from 'react-router-dom';
import { I18nProvider, useI18n } from './i18n';
import { ConfirmProvider, LanguageSelect, SaveIndicator, ToastProvider } from './components/common';
import { TournamentListPage } from './pages/TournamentListPage';
import { NewTournamentPage, TournamentSettingsPage } from './pages/SettingsPage';
import { TournamentLayout, useTournamentHeader } from './pages/TournamentLayout';
import { StandingsPage } from './pages/StandingsPage';
import { PairingsPage } from './pages/PairingsPage';
import { PlayersPage } from './pages/PlayersPage';
import './styles.css';

function Shell() {
  const { t } = useI18n();
  const tournamentName = useTournamentHeader();
  return (
    <div className="app">
      <header className="app-header">
        <Link to="/" className="brand" aria-label={t('app.home')}>
          <img src="/favicon.svg" alt="" width={26} height={26} />
          <span>{t('app.name')}</span>
        </Link>
        <nav className="header-nav">
          <Link to="/">{t('nav.tournaments')}</Link>
          {tournamentName && <span className="header-tournament" title={tournamentName}>{tournamentName}</span>}
        </nav>
        <div className="header-right">
          <SaveIndicator />
          <LanguageSelect />
        </div>
      </header>
      <main className="app-main">
        <Outlet />
      </main>
    </div>
  );
}

function NotFound() {
  const { t } = useI18n();
  return (
    <div className="empty-state">
      <p>{t('error.notFound')}</p>
      <Link to="/">{t('nav.backToTournaments')}</Link>
    </div>
  );
}

// A data router is required for navigation blocking (unsaved settings guard).
const router = createBrowserRouter([
  {
    element: <Shell />,
    children: [
      { path: '/', element: <TournamentListPage /> },
      { path: '/new', element: <NewTournamentPage /> },
      {
        path: '/t/:id',
        element: <TournamentLayout />,
        children: [
          { index: true, element: <Navigate to="standings" replace /> },
          { path: 'standings', element: <StandingsPage /> },
          { path: 'pairings', element: <PairingsPage /> },
          { path: 'players', element: <PlayersPage /> },
          { path: 'settings', element: <TournamentSettingsPage /> },
        ],
      },
      { path: '*', element: <NotFound /> },
    ],
  },
]);

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <I18nProvider>
      <ToastProvider>
        <ConfirmProvider>
          <RouterProvider router={router} />
        </ConfirmProvider>
      </ToastProvider>
    </I18nProvider>
  </StrictMode>,
);
