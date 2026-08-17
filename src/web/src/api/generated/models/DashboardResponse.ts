/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { DashboardAlertResponse } from './DashboardAlertResponse';
import type { DashboardCurrentWeekResponse } from './DashboardCurrentWeekResponse';
import type { DashboardDailyDistanceResponse } from './DashboardDailyDistanceResponse';
import type { DashboardNextSessionResponse } from './DashboardNextSessionResponse';
import type { DashboardPillarResponse } from './DashboardPillarResponse';
import type { DashboardQuotaResponse } from './DashboardQuotaResponse';
import type { DashboardRecoveryResponse } from './DashboardRecoveryResponse';
import type { DashboardTrendWeekResponse } from './DashboardTrendWeekResponse';
export type DashboardResponse = {
    asOf: string;
    windowWeeks: number | string;
    nextSession: (null | DashboardNextSessionResponse);
    currentWeek: DashboardCurrentWeekResponse;
    latestRecovery: (null | DashboardRecoveryResponse);
    dailyDistances: Array<DashboardDailyDistanceResponse>;
    trends: Array<DashboardTrendWeekResponse>;
    latestPillars: Array<DashboardPillarResponse>;
    alerts: Array<DashboardAlertResponse>;
    freeTier: DashboardQuotaResponse;
};

