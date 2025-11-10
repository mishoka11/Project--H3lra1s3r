import http from 'k6/http';
import { check, sleep } from 'k6';
import { Trend, Rate } from 'k6/metrics';

// Metrics
export let latency = new Trend('request_latency');
export let successRate = new Rate('success_rate');

export let options = {
    stages: [
        { duration: '10s', target: 10 },
        { duration: '30s', target: 50 },
        { duration: '20s', target: 0 },
    ],
    thresholds: {
        success_rate: ['rate>0.95'],
        request_latency: ['p(95)<500'],
    },
};

export default function () {
    const catalogUrl = 'http://aa52acadb1ab14532bafc2296357a742-1919469305.eu-central-1.elb.amazonaws.com:8080/api/v1/catalog';
    const designUrl = 'http://af32dd9bd8f1244bebe6a6a482d22600-952858228.eu-central-1.elb.amazonaws.com:8080/api/v1/designs/123';

    const res1 = http.get(catalogUrl);
    const res2 = http.get(designUrl);

    check(res1, { 'catalog 200': (r) => r.status === 200 });
    check(res2, { 'design 200 or 404': (r) => r.status === 200 || r.status === 404 });

    successRate.add(res1.status === 200);
    successRate.add(res2.status === 200);
    latency.add(res1.timings.duration);
    latency.add(res2.timings.duration);

    sleep(1);
}
