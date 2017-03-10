import datetime
import json
import logging
import os
import os.path
import requests
import tempfile
from tumblpy import Tumblpy, TumblpyError
from zipfile import ZipFile, ZipInfo

from appconfig import read_config


def in_date_range(date, start = None, end = None):
    '''
    Check if a date is within a (possibly open) range.
    '''
    logging.warning('Checking if date %s is between %s and %s' % (date, start, end))

    if end is not None and not date < end:
        logging.warning('Date %s is after %s' % (date, end))
        return False
    
    if start is not None and not start <= date:
        logging.warning('Date %s is before %s' % (date, start))
        return False

    return True


def choose_photo_url(photo):
    '''
    Given a Tumblr photo post,
    look at the "original_size" and "alt_sizes"
    to choose the largest file's URL.
    '''
    try:
        'maybe the original is available'
        url = photo['original_size']['url']
        return url

    except KeyError:
        'find the biggest alternate'
        max_height = 0
        url = None

        for alt in photo['alt_sizes']:
            if max_height < alt['height']:
                max_height = alt['height']
                url = alt['url']
        return url


def show_date_range(start_date, end_date):
    '''
    Given a start and end datetime, show the days.
    '''

    ymd = '%Y-%m-%d'
    if start_date is not None:
        start = start_date.strftime(ymd)
    else:
        start = datetime.datetime.utcfromtimestamp(0).strftime(ymd)

    if end_date is not None:
        end = end_date.strftime(ymd)
    else:
        end = datetime.datetime.utcnow().strftime(ymd)

    return '%s_to_%s' % (start, end)


def save_photo_file(archive, prefix, url, id, timestamp):
    '''
    Given a photo URL, download and save it.
    '''

    '''derive it's name and date'''
    date = datetime.datetime.utcfromtimestamp(timestamp)
    metadata = ZipInfo(
            filename = '%s/%s_%s' % (prefix, id, os.path.basename(url)),
            date_time = (date.year, date.month, date.day, date.hour, date.minute, date.second),
        )

    'download it'
    logging.warning('Attempting to download URL: %s' % url)
    photo_data = requests.get(url).content
    logging.warning('Content found, length: %s' % len(photo_data))

    'save it in the archive'
    with archive.open(metadata, 'w') as dl:
        dl.write(photo_data)


class TumblrUtils:
    '''
    A few utilities beyond what Tumblpy provides.
    '''

    def __init__(self, oauth_token, oauth_token_secret, blog_url):
        '''
        Create an API instance for a given Given a pair of (final) oauth creds.
        '''
        conf = read_config()
        self.api = Tumblpy(
                conf['consumer_key'],
                conf['consumer_secret'],
                oauth_token,
                oauth_token_secret,
            )
        self.blog_url = blog_url


    def query_posts(self, post_type = None, start_date = None, end_date = None):
        '''
        Get a bunch of posts.

        Possible types: text, quote, link, answer, video, audio, photo, chat.

        FIXME: the limit and offset properties aren't yet supported,
        so the start and end dates will filter what the API returns naively.
        Apparently only the latest 20 posts, by default!!!
        '''

        logging.warning('Querying for posts in date range between %s and %s.' % (start_date, end_date))
        resp = self.api.posts(blog_url = self.blog_url, post_type = post_type)
        # TODO: add limit and offset to Tumblpy (or remove from Tumblpy docs)
        logging.warning('Filtering from %s posts.' % (len(resp['posts']),))
        #logging.warning(json.dumps(resp['posts'], sort_keys=True, indent=3))
        
        def in_range(post):
            post_date = datetime.datetime.utcfromtimestamp(post['timestamp'])
            return in_date_range(post_date, start_date, end_date)
        matching_posts = list(filter(in_range, resp['posts']))

        logging.warning('Found %s posts.' % (len(matching_posts),))
        return matching_posts


    def save_photos(self, start_date = None, end_date = None):
        '''
        Download and save a bunch of photos.

        Warning: the limit and offset properties aren't yet supported,
        so the start and end dates will filter what the API returns naively.
        '''

        tmpdir = tempfile.mkdtemp(prefix='ninlil_')

        zipfile_prefix = 'tumblr_photos_%s' % (show_date_range(start_date, end_date),)
        zipfile_name = '%s.zip' % (zipfile_prefix,)
        zipfile_path = os.path.join(tmpdir, zipfile_name)

        posts = self.query_posts(
                post_type = 'photo',
                start_date = start_date,
                end_date = end_date
            )

        logging.warning('About to download %s posts.' % len(posts))

        with ZipFile(zipfile_path, 'w') as archive:
            for post in posts:
                logging.warning('About to download %s photos.' % len(list(post['photos'])))

                for photo in post['photos']:
                    url = choose_photo_url(photo)
                    save_photo_file(archive, zipfile_prefix, url, post['id'], post['timestamp'])

        return zipfile_path


    def delete_post(self, id):
        '''
        Delete a post by ID.
        '''
        try:
            # TODO: put this into a new Tumblpy method for post deletion
            return self.api.post(
                    blog_url = self.blog_url,
                    endpoint = 'post/delete',
                    params = { 'id': id }
                )
        except TumblpyError as ex:
            # TODO: put msg and error_code in the Tumblpy docs
            logging.error('Error %s deleting a post (id %s): %s' % (ex.error_code, id, ex.msg))
            return None


    def delete_posts(self, post_type = None, start_date = None, end_date = datetime.datetime(1980,1,1)):
        '''
        Delete a bunch of posts!!

        Warning: the limit and offset properties aren't yet supported,
        so the start and end dates will filter what the API returns naively.
        '''
        posts = list(self.query_posts(
                post_type = post_type,
                start_date = start_date,
                end_date = end_date
            ))

        logging.warning('Deleting %s posts in date range between %s and %s' % (len(posts), start_date, end_date))

        for post in posts:
            self.delete_post(post['id'])
