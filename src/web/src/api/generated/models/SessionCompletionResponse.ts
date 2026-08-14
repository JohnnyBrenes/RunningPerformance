/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { PlannedSessionOutcomeResponse } from './PlannedSessionOutcomeResponse';
import type { SessionActivityCandidateResponse } from './SessionActivityCandidateResponse';
import type { SessionActivityLinkResponse } from './SessionActivityLinkResponse';
import type { SessionCheckinResponse } from './SessionCheckinResponse';
import type { SessionLogicalLoadResponse } from './SessionLogicalLoadResponse';
export type SessionCompletionResponse = {
    plannedSessionId: string;
    scheduledDate: string;
    sessionType: string;
    modality: string | null;
    obligation: string;
    objective: string;
    planVersionStatus: string;
    outcome: (null | PlannedSessionOutcomeResponse);
    links: Array<SessionActivityLinkResponse>;
    candidates: Array<SessionActivityCandidateResponse>;
    checkins: Array<SessionCheckinResponse>;
    load: SessionLogicalLoadResponse;
};

