/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { WeeklyDecisionResponse } from './WeeklyDecisionResponse';
import type { WeeklyEvaluationSessionResponse } from './WeeklyEvaluationSessionResponse';
import type { WeeklyEvaluationSummaryResponse } from './WeeklyEvaluationSummaryResponse';
import type { WeeklyMetricValueResponse } from './WeeklyMetricValueResponse';
export type WeeklyEvaluationDetailResponse = {
    evaluation: WeeklyEvaluationSummaryResponse;
    sessions: Array<WeeklyEvaluationSessionResponse>;
    metrics: Array<WeeklyMetricValueResponse>;
    decision: (null | WeeklyDecisionResponse);
};

