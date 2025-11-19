import http from "k6/http";
import { sleep } from "k6";

export let options = {
    vus: 5,          // 5 virtual users
    duration: "10s", // run for 10 seconds
};

export default function () {
    http.get("http://catalog-service:8080/api/v1/catalog");
    http.get("http://design-service:8080/api/v1/designs");
    http.get("http://order-service:8080/api/v1/orders");
    sleep(0.3);
}
