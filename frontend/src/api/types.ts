// Mirrors the backend JSON contract (camelCase, enums as camelCase strings).

export type TournamentSystem = 'roundRobin' | 'scheveningen' | 'swiss' | 'mcMahon';
export type StoneColor = 'black' | 'white';
export type GameResult = 'unset' | 'blackWins' | 'whiteWins' | 'bothWin' | 'bothLose' | 'draw' | 'unknown';
export type SkippedRoundScoring = 'addHalf' | 'alternate' | 'addNothing';

export interface CriterionSetting { key: string; enabled: boolean }
export interface LayoutColumn { key: string; visible: boolean }
export interface StandingsLayout { columns: LayoutColumn[]; showColors: boolean; hideEmptyColumns: boolean }
export interface PairingConstraints {
  avoidSameCountry: boolean;
  avoidSameClub: boolean;
  avoidSameNationality: boolean;
  avoidSameTeam: boolean;
  avoidSameCoach: boolean;
}
export interface CalculationSettings {
  skippedRoundScoring: SkippedRoundScoring;
  addNothingToOwnCoefficients: boolean;
  addMaximumToOpponentCoefficients: boolean;
  topGroupOnly: boolean;
}

export interface Participant {
  id: number;
  catalogEntryId: string | null;
  localFirstName: string;
  localSurname: string;
  firstName: string;
  surname: string;
  rating: number;
  rank: string;
  rankShift: number;
  grade: string;
  country: string;
  club: string;
  nationality: string;
  team: string;
  coach: string;
  egdPin: number;
  startScore: number;
  preliminary: boolean;
  firstEligibleRound: number;
  /** Draft round-robin starting number (spec 0002); the frozen roster is canonical once drawn. */
  startingNumber: number | null;
  notPlayingRounds: number[];
  comment: string;
  displayName: string;
}

export interface Pairing {
  id: number;
  blackId: number | null;
  whiteId: number | null;
  isBye: boolean;
  boardNumber: number;
  boardNumberFixed: boolean;
  handicap: number;
  additionalKomi: number;
  allowDraw: boolean;
  result: GameResult;
  legacyResultToken: string | null;
  resultByReferee: boolean;
  comment: string;
  protected: boolean;
}

export interface Round { number: number; pairings: Pairing[] }

export interface PairingStage {
  startRound: number;
  /** Null for an imported stage whose legacy system is not identified. */
  system: TournamentSystem | null;
  frozenRosterIds: number[] | null;
  randomStartingNumbers: boolean;
}

export interface Tournament {
  id: string;
  name: string;
  description: string;
  system: TournamentSystem | null;
  legacyKind: number | null;
  boardSize: number;
  firstMoveColor: StoneColor;
  numberOfRounds: number;
  handicapUsed: boolean;
  allowDraws: boolean;
  rankFromRating: boolean;
  customizeCalculation: boolean;
  criteria: CriterionSetting[];
  layout: StandingsLayout;
  pairingConstraints: PairingConstraints;
  calculation: CalculationSettings;
  currentRound: number;
  includeCurrentRound: boolean;
  participants: Participant[];
  rounds: Round[];
  stages: PairingStage[];
  createdAt: string;
  updatedAt: string;
}

export interface TournamentView {
  tournament: Tournament;
  imported: boolean;
  gameCounts: Record<string, number>;
  systemLocked: boolean;
  defaultJoinRound: number | null;
  roundRobin: RoundRobinState | null;
  result?: unknown;
}

export interface TournamentSummary { id: string; name: string; playerCount: number; roundCount: number; updatedAt: string }

export type SettingsInput = Pick<
  Tournament,
  | 'name' | 'description' | 'system' | 'boardSize' | 'firstMoveColor' | 'numberOfRounds' | 'handicapUsed' | 'allowDraws'
  | 'rankFromRating' | 'customizeCalculation' | 'criteria' | 'layout' | 'pairingConstraints' | 'calculation'
>;

export type ParticipantInput = Pick<
  Participant,
  | 'localFirstName' | 'localSurname' | 'firstName' | 'surname' | 'rating' | 'rank' | 'rankShift' | 'grade' | 'country'
  | 'club' | 'nationality' | 'team' | 'coach' | 'preliminary' | 'notPlayingRounds' | 'comment' | 'catalogEntryId'
> & {
  firstEligibleRound: number | null;
  /** null = unchanged, 0 = clear, positive = set (spec 0002). */
  startingNumber?: number | null;
};

export type PairingInput = Omit<Pairing, 'id' | 'legacyResultToken' | 'boardNumber'> & { boardNumber: number | null };

export type CellKind = 'none' | 'game' | 'bye' | 'notPlaying' | 'notJoined';
export type CellOutcome = 'unset' | 'win' | 'loss' | 'draw' | 'bothLose' | 'unknown';

export interface RoundCell {
  round: number;
  kind: CellKind;
  opponentId: number | null;
  opponentPosition: number | null;
  outcome: CellOutcome | null;
  color: StoneColor | null;
  byReferee: boolean;
}

export interface StandingRow {
  position: number;
  place: number;
  placeTo: number;
  participantId: number;
  values: Record<string, number | null>;
  rounds: RoundCell[];
}

export interface Standings {
  displayedRounds: number[];
  countedRounds: number[];
  criteria: { key: string; calculated: boolean }[];
  rows: StandingRow[];
  notices: string[];
}

export interface PreparationRow { position: number; participantId: number; groupScore: number; rounds: RoundCell[] }

export interface Preparation {
  round: number;
  system: TournamentSystem | null;
  displayedRounds: number[];
  scoredRounds: number[];
  rows: PreparationRow[];
  notices: string[];
}

export interface GenerationResult { rounds: number[]; created: number; replaced: number; kept: number; rosterFrozen: boolean; numberOfRounds: number }
export interface ClearResult { deleted: number; byes: number; settingsUnlocked: boolean }
export type StartingNumberSource = 'entered' | 'random';

/** Server read model of the round-robin stage: numbers (frozen or draft), eligibility and the result lock. */
export interface RoundRobinState {
  startRound: number;
  endRound: number;
  frozen: boolean;
  randomStartingNumbers: boolean;
  numbers: Record<string, number | null>;
  eligible: number[];
  lockedBy: { round: number; board: number }[];
}
export interface SkippedParticipant { id: number; name: string; code: string; args: Record<string, unknown> }
export interface FinalizeResult { finalized: number[]; skipped: SkippedParticipant[] }

export interface CatalogEntry {
  id: string;
  localFirstName: string;
  localSurname: string;
  firstName: string;
  surname: string;
  rating: number;
  rank: string;
  grade: string;
  country: string;
  club: string;
  nationality: string;
  team: string;
  coach: string;
  egdPin: number;
  displayName: string;
}

export interface UpsertCandidate { id: string; displayName: string; rating: number; rank: string; club: string }
export interface UpsertPlanItem {
  participantId: number;
  participantName: string;
  action: 'update' | 'create' | 'needsDecision';
  catalogEntryId: string | null;
  candidates: UpsertCandidate[];
}
export interface UpsertPlan { toCreate: number; toUpdate: number; needsDecision: number; items: UpsertPlanItem[] }
export interface UpsertDecision { participantId: number; decision: 'create' | 'link' | 'skip'; catalogEntryId: string | null }
export interface UpsertOutcome { created: number; updated: number; skipped: number }

export interface ExportLimitation { scope: 'tournament' | 'participant' | 'pairing'; field: string; count: number; examples: string[] }
export interface ExportReport {
  blocked: boolean;
  blockingCode: string | null;
  blockingArgs: Record<string, unknown> | null;
  limitations: ExportLimitation[];
}
