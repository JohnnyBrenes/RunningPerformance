/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { ActivitySamplesReviewResponse } from './ActivitySamplesReviewResponse';
import type { QuotaResourceResponse } from './QuotaResourceResponse';
export type DashboardQuotaResponse = {
    billingEnabled: boolean;
    resources: Array<QuotaResourceResponse>;
    activitySamples: ActivitySamplesReviewResponse;
};

