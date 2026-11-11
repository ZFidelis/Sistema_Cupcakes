import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';

export interface ProductReview {
  id: string; productId: string; rating: number; comment: string; createdAt: string; updatedAt: string;
}
@Injectable({ providedIn: 'root' })
export class ReviewsService {
  private readonly http = inject(HttpClient);
  private url(id: string) { return '/api/products/' + encodeURIComponent(id) + '/reviews'; }
  list(id: string) { return firstValueFrom(this.http.get<ProductReview[]>(this.url(id))); }
  mine(id: string) { return firstValueFrom(this.http.get<ProductReview>(this.url(id) + '/mine')); }
  async save(id: string, rating: number, comment: string) {
    const token = await firstValueFrom(this.http.get<{ requestToken: string }>('/api/auth/antiforgery'));
    return firstValueFrom(this.http.put<ProductReview>(this.url(id) + '/mine', { rating, comment }, {
      headers: { 'X-XSRF-TOKEN': token.requestToken },
    }));
  }
}
