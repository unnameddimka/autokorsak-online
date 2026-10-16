import type {
  CatalogEntry, Preparation, StartingNumberSource, TournamentSystem, ExportReport, PairingInput, ParticipantInput, SettingsInput, Standings, TournamentSummary,
  TournamentView, UpsertDecision, UpsertPlan,
} from './types';

/** An error returned by the API as `{ code, args }`; the UI translates `code`. */
export class ApiError extends Error {
  constructor(public readonly code: string, public readonly args: Record<string, unknown>, public readonly status: number) {
    super(code);
  }
}

// ---------- save status (shown in the header) ----------

export type SaveState = { pending: number; failed: boolean; lastSavedAt: number | null };
let saveState: SaveState = { pending: 0, failed: false, lastSavedAt: null };
const listeners = new Set<() => void>();

function setSaveState(next: SaveState) {
  saveState = next;
  listeners.forEach((l) => l());
}

export const saveStatus = {
  get: () => saveState,
  subscribe: (listener: () => void) => {
    listeners.add(listener);
    return () => { listeners.delete(listener); };
  },
};

// ---------- transport ----------

/** 409 answers that only ask the referee to confirm: nothing was attempted, so they are not save failures. */
const CONFIRMATION_REQUESTS = new Set([
  'clear.confirm', 'generate.roundCountAdjustment', 'generate.replaceUnplayed', 'pairing.resultWouldBeDiscarded',
  'stage.unplayedPairingsWillBeDeleted',
]);

async function request<T>(method: string, url: string, body?: unknown): Promise<T> {
  const mutating = method !== 'GET';
  if (mutating) setSaveState({ ...saveState, pending: saveState.pending + 1 });
  let ok = false;
  let confirmationRequest = false;
  try {
    const headers: Record<string, string> = { Accept: 'application/json' };
    if (mutating) headers['X-AutoKorsak-Client'] = '1';
    let payload: BodyInit | undefined;
    if (body instanceof FormData) payload = body;
    else if (body !== undefined) {
      headers['Content-Type'] = 'application/json';
      payload = JSON.stringify(body);
    }
    let response: Response;
    try {
      response = await fetch(url, { method, headers, body: payload });
    } catch {
      throw new ApiError('network.unavailable', {}, 0);
    }
    if (response.status === 204) { ok = true; return undefined as T; }
    const text = await response.text();
    let json: unknown = null;
    try { json = text ? JSON.parse(text) : null; } catch { /* non-JSON error page */ }
    if (!response.ok) {
      const err = json as { code?: string; args?: Record<string, unknown> } | null;
      confirmationRequest = response.status === 409 && CONFIRMATION_REQUESTS.has(err?.code ?? '');
      throw new ApiError(err?.code ?? 'server.error', err?.args ?? {}, response.status);
    }
    ok = true;
    return json as T;
  } finally {
    if (mutating) {
      setSaveState({
        pending: Math.max(0, saveState.pending - 1),
        failed: confirmationRequest ? saveState.failed : !ok,
        lastSavedAt: ok ? Date.now() : saveState.lastSavedAt,
      });
    }
  }
}

const t = (id: string) => `/api/tournaments/${encodeURIComponent(id)}`;
const confirmQuery = (confirm: boolean) => (confirm ? '?confirmDiscardResult=true' : '');

export const api = {
  listTournaments: () => request<TournamentSummary[]>('GET', '/api/tournaments'),
  createTournament: (input: SettingsInput) => request<TournamentView>('POST', '/api/tournaments', input),
  importXml: (file: File) => {
    const form = new FormData();
    form.append('file', file);
    return request<TournamentView>('POST', '/api/tournaments/import', form);
  },
  getTournament: (id: string) => request<TournamentView>('GET', t(id)),
  deleteTournament: (id: string) => request<void>('DELETE', t(id)),
  updateSettings: (id: string, input: SettingsInput) => request<TournamentView>('PUT', `${t(id)}/settings`, input),
  setRound: (id: string, currentRound: number, includeCurrentRound: boolean) =>
    request<TournamentView>('PUT', `${t(id)}/round`, { currentRound, includeCurrentRound }),
  standings: (id: string) => request<Standings>('GET', `${t(id)}/standings`),
  preparation: (id: string) => request<Preparation>('GET', `${t(id)}/standings/preparation`),
  changeSystem: (id: string, startRound: number, system: TournamentSystem, confirmDeleteUnplayed = false) =>
    request<TournamentView>('POST', `${t(id)}/stages`, { startRound, system, confirmDeleteUnplayed }),
  generate: (id: string, round: number, options: { allRounds: boolean; source: StartingNumberSource; confirmReplace?: boolean; roundCount?: number | null }) =>
    request<TournamentView>('POST', `${t(id)}/rounds/${round}/generate`, { confirmReplace: false, roundCount: null, ...options }),
  clearRoundRobin: (id: string, round: number, confirm: boolean) =>
    request<TournamentView>('POST', `${t(id)}/rounds/${round}/clear-round-robin`, { confirm }),
  saveStartingNumbers: (id: string, entries: { participantId: number; number: number | null }[]) =>
    request<TournamentView>('PUT', `${t(id)}/starting-numbers`, { entries }),

  addParticipant: (id: string, input: ParticipantInput) => request<TournamentView>('POST', `${t(id)}/participants`, input),
  updateParticipant: (id: string, pid: number, input: ParticipantInput) =>
    request<TournamentView>('PUT', `${t(id)}/participants/${pid}`, input),
  deleteParticipant: (id: string, pid: number) => request<TournamentView>('DELETE', `${t(id)}/participants/${pid}`),
  finalizePreliminary: (id: string) => request<TournamentView>('POST', `${t(id)}/participants/finalize-preliminary`),
  deletePreliminary: (id: string) => request<TournamentView>('POST', `${t(id)}/participants/delete-preliminary`),

  addPairing: (id: string, round: number, input: PairingInput) =>
    request<TournamentView>('POST', `${t(id)}/rounds/${round}/pairings`, input),
  updatePairing: (id: string, pid: number, input: PairingInput, confirm = false) =>
    request<TournamentView>('PUT', `${t(id)}/pairings/${pid}${confirmQuery(confirm)}`, input),
  deletePairing: (id: string, pid: number, confirm = false) =>
    request<TournamentView>('DELETE', `${t(id)}/pairings/${pid}${confirmQuery(confirm)}`),

  exportReport: (id: string) => request<ExportReport>('GET', `${t(id)}/export-report`),
  exportUrl: (id: string) => `${t(id)}/export.xml`,

  searchCatalog: (search: string) =>
    request<CatalogEntry[]>('GET', `/api/catalog?search=${encodeURIComponent(search)}&limit=20`),
  upsertPlan: (id: string) => request<UpsertPlan>('GET', `${t(id)}/catalog-upsert`),
  applyUpsert: (id: string, decisions: UpsertDecision[]) =>
    request<TournamentView>('POST', `${t(id)}/catalog-upsert`, { decisions }),
};
