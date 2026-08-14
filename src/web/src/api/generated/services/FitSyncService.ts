/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { DeviceCredentialResponse } from '../models/DeviceCredentialResponse';
import type { ExchangePairingTokenRequest } from '../models/ExchangePairingTokenRequest';
import type { FitImportAcceptedResponse } from '../models/FitImportAcceptedResponse';
import type { Stream } from '../models/Stream';
import type { CancelablePromise } from '../core/CancelablePromise';
import { OpenAPI } from '../core/OpenAPI';
import { request as __request } from '../core/request';
export class FitSyncService {
    /**
     * @returns DeviceCredentialResponse OK
     * @throws ApiError
     */
    public static exchangeSyncPairingToken({
        requestBody,
    }: {
        requestBody: ExchangePairingTokenRequest,
    }): CancelablePromise<DeviceCredentialResponse> {
        return __request(OpenAPI, {
            method: 'POST',
            url: '/api/v1/sync/pair',
            body: requestBody,
            mediaType: 'application/json',
            errors: {
                400: `Bad Request`,
            },
        });
    }
    /**
     * @returns FitImportAcceptedResponse Accepted
     * @throws ApiError
     */
    public static enqueueSynchronizedFit({
        requestBody,
        fileName,
        garminActivityId,
    }: {
        requestBody: Stream,
        fileName?: string,
        garminActivityId?: number | string,
    }): CancelablePromise<FitImportAcceptedResponse> {
        return __request(OpenAPI, {
            method: 'POST',
            url: '/api/v1/sync/fit',
            query: {
                'fileName': fileName,
                'garminActivityId': garminActivityId,
            },
            body: requestBody,
            mediaType: 'application/vnd.ant.fit',
            errors: {
                401: `Unauthorized`,
            },
        });
    }
}
