import http from 'k6/http';
import { check, sleep } from 'k6';
import { Trend, Rate } from 'k6/metrics';

// Custom metrics
export let latency = new Trend('request_latency');
export let successRate = new Rate('success_rate');

export let options = {
    stages: [
        { duration: '10s', target: 10 },
        { duration: '30s', target: 50 },
        { duration: '20s', target: 0 },
    ],
    thresholds: {
        success_rate: ['rate>0.95'],     // 95%+ of requests must succeed
        request_latency: ['p(95)<500'],  // 95% < 500ms latency
    },
};

export default function () {
    const res = http.get('http://order-service:8080/api/v1/orders');
    check(res, { 'status is 200': (r) => r.status === 200 });

    successRate.add(res.status === 200);
    latency.add(res.timings.duration);

    sleep(1);
}
