import binascii
import logging
import os


def read_config():
    '''
    Read the consumer key and secret from configuration.
    '''

    flask_secret_key = os.environ['FLASK_SECRET_KEY']
    if flask_secret_key is None:
        logging.warning('using a random flask secret key: please set envvar FLASK_SECRET_KEY')
        flask_secret_key = binascii.hexlify(os.urandom(256))

    return {
        'consumer_key': os.environ['TUMBLR_CONSUMER_KEY'],
        'consumer_secret': os.environ['TUMBLR_CONSUMER_SECRET'],
        'flask_secret_key': flask_secret_key,
    }
