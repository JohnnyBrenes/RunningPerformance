/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { JsonElement } from './JsonElement';
export type PlanAdjustmentResponse = {
    id: string;
    sourcePlanVersionId: string;
    targetPlanVersionId: string;
    targetType: string;
    adjustmentType: string;
    beforeValue: JsonElement;
    afterValue: JsonElement;
    rationale: string;
    reviewCriterion: string;
    createdAt: string;
};

