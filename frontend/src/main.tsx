import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { BrowserRouter, Link, Navigate, Route, Routes } from 'react-router-dom';
import { I18nProvider, useI18n } from './i18n';
import { ConfirmProvider, LanguageSelect, SaveIndicator, ToastProvider } from './components/common';
import { TournamentListPage } from './pages/TournamentListPage';
import { NewTournamentPage } from './pages/SettingsPage';
import { TournamentLayout, useTournamentHeader } from './pages/TournamentLayout';
import { StandingsPage } from './pages/StandingsPage';
import { PairingsPage } from './pages/PairingsPage';
import { PlayersPage } from './pages/PlayersPage';
import { TournamentSettingsPage } from './pages/SettingsPage';
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
        <Routes>
          <Route path="/" element={<TournamentListPage />} />
          <Route path="/new" element={<NewTournamentPage />} />
          <Route path="/t/:id" element={<TournamentLayout />}>
            <Route index element={<Navigate to="standings" replace />} />
            <Route path="standings" element={<StandingsPage />} />
            <Route path="pairings" element={<PairingsPage />} />
            <Route path="players" element={<PlayersPage />} />
            <Route path="settings" element={<TournamentSettingsPage />} />
          </Route>
          <Route path="*" element={<NotFound />} />
        </Routes>
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

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <I18nProvider>
      <ToastProvider>
        <ConfirmProvider>
          <BrowserRouter>
            <Shell />
          </BrowserRouter>
        </ConfirmProvider>
      </ToastProvider>
    </I18nProvider>
  </StrictMode>,
);
