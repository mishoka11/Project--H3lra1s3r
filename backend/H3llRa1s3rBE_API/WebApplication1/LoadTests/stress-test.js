import http from 'k6/http';
import { check, sleep } from 'k6';

export const options = {
    stages: [
        { duration: '30s', target: 10 },
        { duration: '1m', target: 50 },
        { duration: '30s', target: 0 },
    ],
    thresholds: {
        http_req_failed: ['rate<0.01'],
        http_req_duration: ['p(95)<500'],
    },
};

export default function () {
    const catalog = http.get('http://catalog-service:8080/api/v1/catalog');
    const design = http.get('http://design-service:8080/api/v1/designs/123');
    const order = http.get('http://order-service:8080/api/v1/orders/1');

    // ✅ Check responses and print status codes
    console.log(
        `Catalog: ${catalog.status}, Design: ${design.status}, Order: ${order.status}`
    );

    check(catalog, { 'catalog ok': (r) => r.status === 200 });
    check(design, { 'design ok': (r) => r.status === 200 || r.status === 404 });
    check(order, { 'order ok': (r) => r.status === 200 || r.status === 404 });

    sleep(1);
}
