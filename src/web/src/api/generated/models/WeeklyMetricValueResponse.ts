/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { WeeklyMetricEvidenceResponse } from './WeeklyMetricEvidenceResponse';
export type WeeklyMetricValueResponse = {
    id: string;
    metricCode: string;
    dimension: string;
    numericValue: number | string | null;
    booleanValue: boolean | null;
    textValue: string | null;
    unit: string | null;
    status: string;
    formulaVersion: string;
    evidence: Array<WeeklyMetricEvidenceResponse>;
};

