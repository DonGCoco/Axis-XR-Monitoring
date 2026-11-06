import unittest
from unittest.mock import patch
from axis_health import NativeTemperature, finalize, temperature_topics
import axis_proxy_multi as proxy


def camera():
    return dict(cameraId='CAM_001', online=True, temperatureAvailable=True,
                temperature=95, systemReadyAvailable=True, systemReady=True,
                emmcHealthAvailable=True, storageHealthy=True, powerAvailable=True, issues=[])


def event(active, sensor='0'):
    return dict(method='events:notify', params=dict(notification=dict(
        topic='tns1:Device/tnsaxis:Status/tnsaxis:Temperature/tnsaxis:Above',
        message=dict(source=dict(temperature=sensor), data=dict(sensor_level=active)))))


class HealthTests(unittest.TestCase):
    def test_native_alarm_and_recovery(self):
        n=NativeTemperature(); n.reset('CAM_001','connected')
        n.notify('CAM_001', event('1'))
        c=finalize(camera(),n)
        self.assertEqual(c['status'],'WARNING')
        self.assertEqual(c['issues'][0]['type'],'temperature')
        n.notify('CAM_001',event('0'))
        c=finalize(camera(),n)
        self.assertEqual(c['status'],'HEALTHY')  # no invented 70 C rule

    def test_multiple_sensors_and_disconnect(self):
        n=NativeTemperature(); n.reset('CAM_001','connected')
        n.notify('CAM_001',event('1','0')); n.notify('CAM_001',event('0','1'))
        self.assertTrue(finalize(camera(),n)['temperatureAlarmActive'])
        n.reset('CAM_001','unavailable')
        c=finalize(camera(),n)
        self.assertFalse(c['temperatureAlarmAvailable'])
        self.assertEqual(c['issues'][0]['type'],'temperature_monitoring_unavailable')

    def test_combined_issues_and_unknown(self):
        c=camera(); c.update(storageHealthy=False,systemReady=False,temperatureAvailable=False)
        result=finalize(c)
        self.assertEqual({i['type'] for i in result['issues']},
                         {'storage','system_not_ready','temperature_data_unavailable'})

    def test_discovery(self):
        xml='<root xmlns:a="urn:axis" xmlns:t="urn:topic"><a:Device><a:Status><a:Temperature><a:Above t:topic="true"/></a:Temperature></a:Status></a:Device></root>'
        self.assertEqual(temperature_topics(xml),['tns1:Device/tnsaxis:Status/tnsaxis:Temperature/tnsaxis:Above'])
        self.assertEqual(temperature_topics('<root/>'),[])

    def test_failed_camera(self):
        with patch.object(proxy,'json_post',side_effect=TimeoutError):
            c=proxy.read_camera('CAM_001')
        self.assertEqual(c['status'],'OFFLINE')
        self.assertEqual(c['issues'][0]['type'],'connection')

    def test_missing_systemready_not_false_alarm(self):
        responses=[{'data':{'propertyList':{'ProdNbr':'Q1728'}}}, {'error':{'code':1}}, {'data':{}}]
        import json
        with patch.object(proxy,'json_post',side_effect=[json.dumps(x) for x in responses]), patch.object(proxy,'request_text',return_value=''):
            c=proxy.read_camera('CAM_001')
        self.assertFalse(c['systemReadyAvailable'])
        self.assertNotIn('system_not_ready',[i['type'] for i in c['issues']])
        self.assertEqual(c['status'],'WARNING')

if __name__=='__main__': unittest.main()
