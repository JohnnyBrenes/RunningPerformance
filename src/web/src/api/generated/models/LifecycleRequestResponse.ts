/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { JsonElement } from './JsonElement';
export type LifecycleRequestResponse = {
    id: string;
    requestType: string;
    scope: JsonElement;
    rationale: string;
    status: string;
    approvedBy: string | null;
    executedAt: string | null;
    evidence: (null | JsonElement);
    createdAt: string;
    updatedAt: string;
};

