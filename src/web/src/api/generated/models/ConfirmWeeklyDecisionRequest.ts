/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { PlanVersionAdjustmentRequest } from './PlanVersionAdjustmentRequest';
export type ConfirmWeeklyDecisionRequest = {
    decision: string;
    observation: string;
    evidence: string;
    historicalComparison: string;
    interpretation: string;
    recommendation: string;
    planAdjustment: (null | PlanVersionAdjustmentRequest);
};

