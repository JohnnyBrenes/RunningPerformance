/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { ActivitySourceResponse } from './ActivitySourceResponse';
import type { ActivitySummaryResponse } from './ActivitySummaryResponse';
export type ActivityDetailResponse = {
    activity: ActivitySummaryResponse;
    startedAtUtc: string | null;
    timezoneName: string | null;
    utcOffsetMinutes: number | string | null;
    movingSeconds: number | string | null;
    elapsedSeconds: number | string | null;
    averageSpeedMps: number | string | null;
    calories: number | string | null;
    averageCadenceSpm: number | string | null;
    averagePowerW: number | string | null;
    elevationGainM: number | string | null;
    lapCount: number | string | null;
    sources: Array<ActivitySourceResponse>;
};

