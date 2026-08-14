/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { CreatePairingTokenRequest } from '../models/CreatePairingTokenRequest';
import type { PairingTokenResponse } from '../models/PairingTokenResponse';
import type { SyncClientResponse } from '../models/SyncClientResponse';
import type { CancelablePromise } from '../core/CancelablePromise';
import { OpenAPI } from '../core/OpenAPI';
import { request as __request } from '../core/request';
export class SyncClientsService {
    /**
     * @returns PairingTokenResponse OK
     * @throws ApiError
     */
    public static createSyncPairingToken({
        requestBody,
    }: {
        requestBody: CreatePairingTokenRequest,
    }): CancelablePromise<PairingTokenResponse> {
        return __request(OpenAPI, {
            method: 'POST',
            url: '/api/v1/sync-clients/pairing-tokens',
            body: requestBody,
            mediaType: 'application/json',
        });
    }
    /**
     * @returns SyncClientResponse OK
     * @throws ApiError
     */
    public static listSyncClients(): CancelablePromise<Array<SyncClientResponse>> {
        return __request(OpenAPI, {
            method: 'GET',
            url: '/api/v1/sync-clients',
        });
    }
    /**
     * @returns void
     * @throws ApiError
     */
    public static revokeSyncClient({
        id,
    }: {
        id: string,
    }): CancelablePromise<void> {
        return __request(OpenAPI, {
            method: 'DELETE',
            url: '/api/v1/sync-clients/{id}',
            path: {
                'id': id,
            },
            errors: {
                404: `Not Found`,
            },
        });
    }
}
