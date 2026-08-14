/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { DashboardModalityTrendResponse } from './DashboardModalityTrendResponse';
import type { DashboardSourceResponse } from './DashboardSourceResponse';
export type DashboardTrendWeekResponse = {
    weekStart: string;
    weekEnd: string;
    evaluationId: string | null;
    trafficLight: string | null;
    srpeTotal: number | string | null;
    modalities: Array<DashboardModalityTrendResponse>;
    sources: Array<DashboardSourceResponse>;
    evaluationHref: string | null;
};

