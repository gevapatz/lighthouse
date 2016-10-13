ifeq ("$(strip $(CODK_DIR))", "")
  $(error Please set the CODK_DIR variable.)
endif

ARDUINOSW_DIR ?= $(CODK_DIR)/arc

current_dir = $(shell pwd)

VERBOSE  = true

LIBDIRS  = 	$(ARDUINOSW_DIR)/corelibs/libraries/CurieI2S/src \
		$(ARDUINOSW_DIR)/corelibs/libraries/CurieBLE/src \
		$(ARDUINOSW_DIR)/corelibs/libraries/CurieIMU/src \
		$(ARDUINOSW_DIR)/corelibs/libraries/Wire/src	\
		$(ARDUINOSW_DIR)/corelibs/libraries/SPI/src	

include $(ARDUINOSW_DIR)/Makefile.inc

all: compile

.DEFAULT_GOAL := all
