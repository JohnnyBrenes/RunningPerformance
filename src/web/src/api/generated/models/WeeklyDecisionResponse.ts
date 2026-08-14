/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { PlanAdjustmentResponse } from './PlanAdjustmentResponse';
export type WeeklyDecisionResponse = {
    id: string;
    decision: string;
    observation: string;
    evidence: string;
    historicalComparison: string;
    interpretation: string;
    recommendation: string;
    confirmedBy: string;
    confirmedAt: string;
    adjustments: Array<PlanAdjustmentResponse>;
};

