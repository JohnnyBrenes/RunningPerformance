/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { DashboardModalityTrendResponse } from './DashboardModalityTrendResponse';
import type { DashboardSourceResponse } from './DashboardSourceResponse';
export type DashboardDailyDistanceResponse = {
    date: string;
    modalities: Array<DashboardModalityTrendResponse>;
    sources: Array<DashboardSourceResponse>;
};

