import {
  ApiResponseData,
  ApiResponseList,
  ApiResponsePaged
} from '../../models/api-response';

export function normalizeArray<T>(
  resp?: ApiResponseData<T> | ApiResponseList<T> | ApiResponsePaged<T> | T[]
): T[] {
  if (!resp) return [];

  if (Array.isArray(resp)) return resp;

  if (typeof resp === 'object') {
    if ('items' in resp && Array.isArray(resp.items)) return resp.items;
    if ('data' in resp && Array.isArray(resp.data)) return resp.data;
  }

  return [];
}
