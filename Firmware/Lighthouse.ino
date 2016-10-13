#include <CurieI2S.h>
#include <Wire.h>
#include <SPI.h>
#include <CurieBLE.h>
#include <CurieIMU.h>

#define PROGRAM_FPGA
#define SENSOR_BUF_SIZE 128
#define TIMING_BUF_SIZE 40

//#define DEBUG

#ifdef PROGRAM_FPGA
// generate bitmap using convert_bitmap.pl

#include "fpga_bitmap.h"
#endif

// The Intel Curie datasheet claims that the 32 MHz system clock is exported
// on pin PLT_CLK_0 (ball E3, routed to Arduino pin 8 on the 101 board) but 
// the Intel firmware refers to this pin as a low speed (32.768 kHz) clock
// pin. Experimentally, when configured as a clock output I was only able
// to observe a 32 kHz clock on this pin, so Fake_I2S is a hack to generate
// a timebase for the FPGA PLL. The hardware I2S is driven at an intentionally
// 'overclocked' 16 MHz. This generates an ugly but stable signal which the
// PLL cleans up and outputs as a 48 MHz base clock signal.

class Fake_I2S : public Curie_I2S {
	void enableInterrupts() {
		// we don't need interrupts, and we don't want to call the code 
		// in the stock ISR which turns of the clock when the Tx buffers
		// are empty, so we override this to disable interrupts
		interrupt_disable(IRQ_I2S_INTR);
	}
};

Fake_I2S FakeI2S;
//I2S_TX -> Pin 7
//I2S_TSK -> Pin 4
//I2S_TSCK -> pin 2

#define ADDR 0x55
#define SS_PIN 10

#define FPGA_RESET A4
#define FPGA_CSN A5	

BLEPeripheral blePeripheral;      
BLEService timerService("19b10010-E8F2-537E-4F6C-D104768A0116");
BLECharacteristic timerCharacteristic("19b10012-E8F2-537E-4F6C-D104768A0116", BLERead | BLENotify, 20);  
BLECharacteristic imuCharacteristic("19b10014-E8F2-537E-4F6C-D104768A0116", BLERead | BLENotify, 16);                              
   
long previousMillis = 0; 


uint32_t sensor_buf[SENSOR_BUF_SIZE];
uint8_t timing_buf[TIMING_BUF_SIZE];
uint32_t timing_data[6];

int imu_buf[8];
struct sensor_state {
	uint32_t v_a;
	uint32_t h_a;
	uint32_t v_b;
	uint32_t h_b;
	uint32_t cnt;
	byte state;
	byte is_vert;
	byte is_b;
	
	sensor_state() {
		is_vert = state = is_b = 0;
		cnt = v_b = h_b = v_a = h_a = 0;
	}
};

sensor_state sensors[10];

void setup()  {
	pinMode(A2, OUTPUT);
#ifdef DEBUG
	digitalWrite(A2, HIGH);
#else
	digitalWrite(A2, LOW);
#endif
	SPI.begin();
	SPI.setClockDivider(8);
	pinMode(SS_PIN, OUTPUT);
	digitalWrite(SS_PIN, HIGH);
	
	Serial.begin(115200); // initialize Serial communication
	//while(!Serial) ;      // wait for serial port to connect.
	delay(1500);
		
	blePeripheral.setLocalName("TrackedObj");
	blePeripheral.setConnectionInterval(6, 13); // ~= 120/60 Hz in 1.25 msec intervals
	blePeripheral.setAdvertisedServiceUuid(timerService.uuid());
	blePeripheral.addAttribute(timerService);
	blePeripheral.addAttribute(timerCharacteristic);
	blePeripheral.addAttribute(imuCharacteristic);
	for(byte i=0;i<SENSOR_BUF_SIZE;++i)
		sensor_buf[i] = 0;
	for(byte i=0;i<6;++i)
		timing_data[i] = 0;
	for(byte i=0;i<TIMING_BUF_SIZE;++i)
		timing_buf[i] = 0;
	for(byte i=0;i<8;++i)
		imu_buf[i] = 0;
	timerCharacteristic.setValue((unsigned char *)sensor_buf, 20);
	imuCharacteristic.setValue((unsigned char *)sensor_buf, 16);

	Serial.println("Starting");
	// advertise the service
	blePeripheral.begin();
	CurieIMU.begin();
	// I2S has to start AFTER BLE or the chip hangs!
	FakeI2S.begin(0x00020002, I2S_32bit);
	FakeI2S.setI2SMode(PHILIPS_MODE);
	FakeI2S.initTX();
	//start filling the tx buffer
	FakeI2S.pushData(0xFFFFFFFF);
	FakeI2S.pushData(0x00000000);
	FakeI2S.pushData(0xDEADFACE);
	FakeI2S.pushData(0x10101010);
	//Start Transmission
	FakeI2S.startTX();
	
	// drive A5 high with the 101 board on a stable surface to 
	// calibrate the IMU
	pinMode(A5, INPUT);
	digitalWrite(A5, HIGH);
	if(digitalRead(A5)==LOW) {
		Serial.println("Calibrating IMU");
		CurieIMU.autoCalibrateGyroOffset();
		CurieIMU.autoCalibrateAccelerometerOffset(X_AXIS, 0);
    	CurieIMU.autoCalibrateAccelerometerOffset(Y_AXIS, 0);
    	CurieIMU.autoCalibrateAccelerometerOffset(Z_AXIS, 1);
		Serial.println("Calibration finished");
		while(digitalRead(A5)==LOW)
			;
	}
	digitalWrite(A5, LOW);
	
#ifdef PROGRAM_FPGA	
	SPI.setClockDivider(8);
	pinMode(FPGA_RESET, OUTPUT);
	pinMode(FPGA_CSN, OUTPUT);
	Serial.println("Programming FPGA");
	digitalWrite(FPGA_CSN, LOW);
	digitalWrite(FPGA_RESET, LOW);
	delay(1);
	digitalWrite(FPGA_RESET, HIGH);
	delay(1);
	SPI.transfer(0); // 8 clocks to start
	for(unsigned long i=0; i<fpga_bitmap_len; ++i)
		SPI.transfer(pgm_read_byte_near(fpga_bitmap + i));
	// 100 clocks idle
	for(int i=0; i<13; ++i) 
		SPI.transfer(0);
	digitalWrite(FPGA_CSN, HIGH);
	Serial.println("Programming complete");
#endif

	SPI.setClockDivider(8);

	digitalWrite(SS_PIN, LOW);
	SPI.transfer(0xbf);
	digitalWrite(SS_PIN, HIGH);
	digitalWrite(SS_PIN, LOW);
	Serial.print(SPI.transfer(0xff), HEX);
	Serial.print(' ');
	Serial.print(SPI.transfer(1), HEX);
	Serial.print(' ');
	Serial.print(SPI.transfer(1), HEX);
	Serial.print(' ');
	Serial.print(SPI.transfer(1), HEX);
	Serial.print(' ');
	Serial.println(SPI.transfer(1), HEX);
	digitalWrite(SS_PIN, HIGH);
}

byte pin = 0;
byte v = 0;
void loop() {
	BLECentral central = blePeripheral.central();

  if (central) {
#ifdef DEBUG
    Serial.print("Connected to central [DBG]: ");
#else
	  Serial.print("Connected to central: ");
#endif
    // print the central's MAC address:
    Serial.println(central.address());

    while (central.connected()) {
        update_timers();
		update_imu();
    }
    Serial.print("Disconnected from central: ");
    Serial.println(central.address());
  }
}

uint32_t get_timer(byte idx) {
	uint32_t rc = 0;
	digitalWrite(SS_PIN, LOW);
	SPI.transfer(idx);
	digitalWrite(SS_PIN, HIGH);
	digitalWrite(SS_PIN, LOW);
	SPI.transfer(idx);
	for(byte i=0; i<4;++i) {
		byte v = SPI.transfer(idx);
		rc = (rc << 8) | v;
	}
	digitalWrite(SS_PIN, HIGH);
	return rc;
}

// note that B precedes A in the timing sequence 

#define STATE_IDLE 				0
#define	STATE_WAIT_A_LIVE		1
#define STATE_WAIT_A_SILENCE	2
#define STATE_WAIT_A_PULSE 		3
#define STATE_WAIT_B_SILENCE_1	4
#define STATE_WAIT_A_QUIET		5
#define STATE_WAIT_B_SILENCE_2	6
#define STATE_WAIT_B_PULSE		7

void update_timers() {
	byte i = 0;
	uint16_t changed[2] = { 0x0000, 0x8000};
	bool got_all = true;
	while(i<SENSOR_BUF_SIZE) {
		uint32_t val = get_timer(0);
		if(val>0) 
			sensor_buf[i++] = val;
		else if(i>0) {// val == 0
			while(i<SENSOR_BUF_SIZE)
				sensor_buf[i++] = 0;
			got_all = true;
		}
	}
	for(byte i=0;i<SENSOR_BUF_SIZE && sensor_buf[i]!=0;++i) {
		byte sensor = (sensor_buf[i]&0x0f);
		uint32_t ticks =  (sensor_buf[i]>>24) | (((sensor_buf[i]>>16)&0xff) << 8) | 
							(((sensor_buf[i]>>8)&0xff) << 16);
		bool is_high = (sensor_buf[i]&0x40);
#ifdef DEBUG
		if(i<100)
			Serial.print("0");
		if(i<10)
			Serial.print("0");
		Serial.print(i);
		Serial.print(": ");
		if(sensor == 2) {
			Serial.print(sensor_buf[i], HEX);
			Serial.print((sensor_buf[i]&0x40)?" H ":" L ");
			Serial.print(((float)val)/48.0);
		} else {
			Serial.print(sensor_buf[i], HEX);	
		}
		Serial.println(" ");
#endif
		if(is_high) {
			if(sensors[sensor].state == STATE_WAIT_B_SILENCE_1) {
				sensors[sensor].cnt += ticks;
				sensors[sensor].state = STATE_WAIT_A_QUIET;
			} else if(sensors[sensor].state == STATE_WAIT_B_SILENCE_2) {
				sensors[sensor].cnt += ticks;
				sensors[sensor].state = STATE_WAIT_B_PULSE;
			} else if(sensors[sensor].state == STATE_WAIT_A_SILENCE) {
				sensors[sensor].cnt = ticks;
				sensors[sensor].state = STATE_WAIT_A_PULSE;
			}
		} else {
			// low - we have a pulse
			if(ticks<2000) {
				if(sensors[sensor].state == STATE_WAIT_A_PULSE || 
				   sensors[sensor].state == STATE_WAIT_B_PULSE) {
					if(sensors[sensor].is_b) {
						changed[0] |= (1<<sensor);
						if(sensors[sensor].is_vert)
							sensors[sensor].v_b = sensors[sensor].cnt;
						else
							sensors[sensor].h_b = sensors[sensor].cnt;
					} else {
						changed[1] |= (1<<sensor);
						if(sensors[sensor].is_vert)
							sensors[sensor].v_a = sensors[sensor].cnt;
						else
							sensors[sensor].h_a = sensors[sensor].cnt;
					}
				} 
				sensors[sensor].cnt = 0;
				// go back to idle no matter what - if we weren't expecting a 
				// short pulse then we're in some dodgy error state anyway
				sensors[sensor].state = STATE_IDLE;
			} else if ((ticks > 2800 && ticks < 3200) || (ticks > 3800 && ticks < 4200) ||
				(ticks > 3300 && ticks < 3700) || (ticks > 4300 && ticks < 4700)) {
				// rotor 0 start or rotor 1 start 
				sensors[sensor].is_vert = ((ticks > 3300 && ticks < 3700) || 
										   (ticks > 4300 && ticks < 4700));
				sensors[sensor].cnt = 0;
				if(sensors[sensor].state == STATE_WAIT_A_LIVE) {
					sensors[sensor].state = STATE_WAIT_A_SILENCE;	
				} else {
					sensors[sensor].state = STATE_WAIT_B_SILENCE_1;	
					sensors[sensor].is_b = true;
				}
			} else if ((ticks > 4800 && ticks < 5200) || (ticks > 5800 && ticks < 6200) ||
					   (ticks > 5300 && ticks < 5700) || (ticks > 6300 && ticks < 6700) ) {
				// skip states
				if(sensors[sensor].state == STATE_IDLE) {
					sensors[sensor].state = STATE_WAIT_A_LIVE;
					sensors[sensor].is_b = false;
				} else if(sensors[sensor].state == STATE_WAIT_A_QUIET) {
					sensors[sensor].cnt += ticks;
					sensors[sensor].state = STATE_WAIT_B_SILENCE_2;
				} else {
					// something weird happened - give up
					sensors[sensor].state = STATE_IDLE;
				}
			} // end if ticks == 
		} // end if hi/lo
	}
	for(byte i = 0;i<18;++i) { 
		byte shift = (2 - (i%3)) * 8;
		timing_buf[i] = ((i/3)%2)==0?((sensors[i/6].h_b>>shift) & 0xff): 
			((sensors[i/6].v_b>>shift) & 0xff);
	}
	timing_buf[18] = changed[0]>>8;
	timing_buf[19] = changed[0]&0xff;
	timerCharacteristic.setValue((unsigned char *)timing_buf, 20);
	for(byte i = 0;i<18;++i) {
		byte shift = (2 - (i%3)) * 8;
		timing_buf[i] = ((i/3)%2)==0?((sensors[i/6].h_a>>shift) & 0xff): 
			((sensors[i/6].v_a>>shift) & 0xff);
	}
	timing_buf[18] = changed[1]>>8;
	timing_buf[19] = changed[1]&0xff;
	timerCharacteristic.setValue((unsigned char *)timing_buf, 20);
}

void update_imu() {
	CurieIMU.readMotionSensor(imu_buf[0], imu_buf[1], imu_buf[2],
							  imu_buf[3], imu_buf[4], imu_buf[5]);
	*(long *)(&(imu_buf[6])) = millis();
	imuCharacteristic.setValue((unsigned char *)imu_buf, 16);
}


