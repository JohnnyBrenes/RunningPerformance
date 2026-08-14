/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { PlannedSessionBlockResponse } from './PlannedSessionBlockResponse';
export type PlannedSessionResponse = {
    id: string;
    scheduledDate: string;
    sessionType: string;
    modality: string | null;
    obligation: string;
    objective: string;
    distanceM: number | string | null;
    durationSeconds: number | string | null;
    targetRpeMin: number | string | null;
    targetRpeMax: number | string | null;
    terrain: string | null;
    warmup: string | null;
    mainSet: string | null;
    recoveries: string | null;
    cooldown: string | null;
    blocks: Array<PlannedSessionBlockResponse>;
};

