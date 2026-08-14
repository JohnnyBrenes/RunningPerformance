/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { JsonElement } from './JsonElement';
import type { SessionActivityResponse } from './SessionActivityResponse';
export type SessionActivityLinkResponse = {
    id: string;
    method: string;
    criteria: JsonElement;
    confidence: number | string | null;
    status: string;
    supersedesId: string | null;
    createdAt: string;
    updatedAt: string;
    activity: SessionActivityResponse;
};

